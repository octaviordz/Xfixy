namespace Xfixy

open System
open System.Collections
open System.IO
open System.Threading
open System.Management.Automation

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
                // print the resulting pipeline objects to the console.
                return Result.Ok pipelineObjects
            with
            | ex -> return Result.Error ex
        }

    let loadScriptsAsync (scriptFiles: string array) (ct: CancellationToken) =
        task {
            let res = Generic.Dictionary<string, string>()

            for scriptFile in scriptFiles do
                let fi = FileInfo(scriptFile)
                //printfn $"Loading script file: {fi.FullName}"
                if fi.Exists then
                    let! content = File.ReadAllTextAsync(fi.FullName, ct)
                    //printfn $"Content loaded: {content}"
                    res.Add(fi.FullName, content)

            return res :> Generic.IDictionary<string, string>
        }

type internal Messager() =
    let observers = Generic.List<IObserver<string>>()

    member public _.Push(message: string) =
        for observer in observers do
            observer.OnNext message

    interface IObservable<string> with
        member _.Subscribe(observer: IObserver<string>) : IDisposable =
            if not (observers.Contains observer) then
                observers.Add observer

            new Unsubscriber<string>(observers, observer)

and internal Unsubscriber<'T>(observers: Generic.List<IObserver<'T>>, observer: IObserver<'T>) =
    interface IDisposable with
        member _.Dispose() : unit =
            if
                not (isNull observer)
                && observers.Contains observer
            then
                observers.Remove observer |> ignore

module Control =
    let runScriptAsync (scriptContentDict: Generic.IDictionary<string, string>) (ct: CancellationToken) =
        task {
            let result = Generic.Dictionary<string, Result<string, string>>()

            for kv in scriptContentDict do
                let scriptContent = kv.Value
                let parameters: IDictionary = Generic.Dictionary<string, obj>()
                let! res = PSscript.runScriptAsync scriptContent parameters ct

                match res with
                | Result.Ok psDataCollection ->
                    for item in psDataCollection do
                        let psObjRes = item.BaseObject.ToString()
                        let! res = PSscript.runScriptAsync scriptContent parameters ct
                        result.Add(kv.Key, (Result.Ok psObjRes))
                | Result.Error ex ->
                    result.Add(kv.Key, (Result.Error $"""Error running "{kv.Key}". Error: {ex.GetType()}."""))

            return result
        }
