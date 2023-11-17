module Xfixy.Control

open System
open System.Collections
open System.Collections.Generic
open System.IO
open System.Threading
open System.Management.Automation
open System.Diagnostics
open Xfixy.Common

module internal PSscript =
    /// <summary>
    /// Runs a PowerShell script with parameters and prints the resulting pipeline objects to the console output.
    /// </summary>
    /// <param name="scriptContent">The script file contents.</param>
    /// <param name="scriptParameters">A dictionary of parameter names and parameter values.</param>
    let runScriptAsync (scriptContent: string) (scriptParameters: IDictionary) (ct: CancellationToken) =
        task {
            // https://keithbabinec.com/2020/02/15/how-to-run-powershell-core-scripts-from-net-core-applications/
            // create a new hosted PowerShell instance using the default runspace.
            // wrap in a using statement to ensure resources are cleaned up.
            use ps = PowerShell.Create()
            // specify the script code to run.
            ps.AddScript(scriptContent) |> ignore
            // specify the parameters to pass into the script.
            ps.AddParameters(scriptParameters) |> ignore
            // execute the script and await the result.
            try
                let! pipelineObjects =
                    ps
                        .InvokeAsync()
                        .WaitAsync(ct)
                        .ConfigureAwait(false)

                return Result.Ok pipelineObjects
            with
            | ex -> return Result.Error ex
        }

    let loadScriptsAsync (scriptFiles: string array) (ct: CancellationToken) =
        task {
            let res = Dictionary<string, string>()

            for scriptFile in scriptFiles do
                let fi = FileInfo(scriptFile)
                //printfn $"Loading script file: {fi.FullName}"
                if fi.Exists then
                    let! content = File.ReadAllTextAsync(fi.FullName, ct)
                    //printfn $"Content loaded: {content}"
                    res.Add(fi.FullName, content)

            return res :> IDictionary<string, string>
        }

type internal Messager() =
    let observers = ResizeArray<IObserver<Note>>()

    member public _.Push(message: Note) =
        for observer in observers do
            observer.OnNext message

    interface IObservable<Note> with
        member _.Subscribe(observer: IObserver<Note>) : IDisposable =
            if not (observers.Contains observer) then
                observers.Add observer

            new Unsubscriber<Note>(observers, observer)

and internal Unsubscriber<'T>(observers: ResizeArray<IObserver<'T>>, observer: IObserver<'T>) =
    interface IDisposable with
        member _.Dispose() : unit =
            if
                not (isNull observer)
                && observers.Contains observer
            then
                observers.Remove observer |> ignore

let runScriptAsync (scriptContentDict: IDictionary<string, string>) (ct: CancellationToken) =
    task {
        let result = Dictionary<string, Result<string, string>>()

        for kv in scriptContentDict do
            let scriptContent = kv.Value
            let parameters: IDictionary = Dictionary<string, obj>()
            let! res = PSscript.runScriptAsync scriptContent parameters ct

            match res with
            | Result.Ok psDataCollection ->
                for item in psDataCollection do
                    let psObjRes =
                        match item with
                        | null -> String.Empty
                        | _ -> item.BaseObject.ToString()
                    result.Add(kv.Key, (Result.Ok psObjRes))
            | Result.Error ex ->
                result.Add(kv.Key, (Result.Error $"""Error running "{kv.Key}". Error: {ex.GetType()}."""))

        return result
    }

[<AutoOpen>]
module internal ExeLocation =
    let workerProcessName = "Xfixy.Worker"
    let workerExe = "Xfixy.Worker.exe"

    let baseLocation =
        DirectoryInfo(
            AppDomain.CurrentDomain.BaseDirectory
        )
            .Parent
            .FullName

    let workerExec =
        [ Path.Combine(baseLocation, "Xfixy.Worker", workerExe)
          Path.Combine(baseLocation, workerExe) ]
        |> List.tryFind (fun it -> File.Exists(it))

let getProcess processName =
    try
        Process.GetProcessesByName processName
        |> Array.tryHead
    with
    | ex ->
        Log.Trace(ex, (sprintf "Unable to get process %s" processName))
        Option.None

[<RequireQualifiedAccess>]
type WorkerProcessStatus =
    | Running of Process
    | Stopped

[<CompiledName("CheckWorkerProcess")>]
let checkWorkerProcess handle =
    match getProcess workerProcessName with
    | Some p -> handle (WorkerProcessStatus.Running p)
    | None -> handle WorkerProcessStatus.Stopped

    ()

[<CompiledName("StartStopWorkerProcess")>]
let startStopWorkerProcess () =
    checkWorkerProcess (fun status ->
        match status with
        | WorkerProcessStatus.Running proc -> proc.Kill(true)
        | WorkerProcessStatus.Stopped ->
            match workerExec with
            | None -> ()
            | Some workerExec ->
                let info = ProcessStartInfo()
                info.FileName <- workerExec
                let p = info |> Process.Start
                p.StartTime |> ignore
                ())
