namespace Xfixy

open System
open System.Collections
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open System.Management.Automation
open Microsoft.Extensions.Configuration


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
            let! pipelineObjects =
                ps
                    .InvokeAsync()
                    .WaitAsync(ct)
                    .ConfigureAwait(false)
            // print the resulting pipeline objects to the console.
            return pipelineObjects
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

            new Unsubscriber(observers, observer)

and internal Unsubscriber(observers: Generic.List<IObserver<string>>, observer: IObserver<string>) =
    interface IDisposable with
        member self.Dispose() : unit =
            if
                not (isNull observer)
                && observers.Contains observer
            then
                observers.Remove observer |> ignore

[<AutoOpen>]
module internal Control =
    type FetchScript =
        | Fetch
        | Not

// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let messageTracker = Messager()

    member self.OnMessage
        with public get (): IObservable<string> = messageTracker

    member _.Delay =
        let delay = configuration.GetValue<int>("Worker:Delay", 3000)
        delay

    member _.ScriptReloadInterval =
        let delay = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
        delay

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            let stopWatch = Stopwatch()
            let scriptsPath = configuration["Worker:Scripts-Path"]
            let mutable cached = dict []

            while not ct.IsCancellationRequested do
                logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)

                let fetchOrNot =
                    if not stopWatch.IsRunning then
                        stopWatch.Start()
                        Fetch
                    elif stopWatch.Elapsed.TotalSeconds
                         >= self.ScriptReloadInterval then
                        Fetch
                    else
                        Not

                let! scriptContentDict =
                    task {
                        match fetchOrNot with
                        | Fetch ->
                            stopWatch.Restart()
                            let scriptFiles = Directory.GetFiles scriptsPath
                            let! res = PSscript.loadScriptsAsync scriptFiles ct
                            cached <- res
                            return res
                        | Not -> return cached
                    }

                for kv in scriptContentDict do
                    let scriptContent = kv.Value
                    let parameters: IDictionary = Generic.Dictionary<string, obj>()
                    let! psDataCollection = PSscript.runScriptAsync scriptContent parameters ct

                    for item in psDataCollection do
                        let psObjRes = item.BaseObject.ToString()
                        logger.LogInformation("PowerShell Result: {PSObject}", psObjRes)
                        messageTracker.Push psObjRes

                do! Task.Delay(self.Delay, ct)
        }
