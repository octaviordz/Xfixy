namespace Xfixy

open System
open System.Collections
open System.Diagnostics
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open System.Management.Automation
open Microsoft.Extensions.Configuration

module internal Task =
    let map mapper task =
        TaskBuilder.task {
            let! v = task
            return (mapper v)
        }

    let mapOption mapper taskOption =
        taskOption
        |> Option.map (fun task ->
            task
            |> map (fun option -> option |> Option.map mapper))


module internal Option =
    let mapTask mapper taskOption =
        taskOption
        |> Option.map (fun v -> v |> Task.map mapper)

type internal MessageTracker() =
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
// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let messageTracker = MessageTracker()

    member self.OnMessage
        with public get (): IObservable<string> = messageTracker

    /// <summary>
    /// Runs a PowerShell script with parameters and prints the resulting pipeline objects to the console output.
    /// </summary>
    /// <param name="scriptContent">The script file contents.</param>
    /// <param name="scriptParameters">A dictionary of parameter names and parameter values.</param>
    member private self.runScript (scriptContent: string) (scriptParameters: IDictionary) (ct: CancellationToken) =
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
            for item in pipelineObjects do
                let psObjRes = item.BaseObject.ToString()
                logger.LogInformation("PowerShell Result: {PSObject}", psObjRes)
                messageTracker.Push psObjRes
        }

    member _.Delay =
        let delay = configuration.GetValue<int>("Worker:Delay", 3000)
        delay

    member _.ScriptReloadInterval =
        let delay = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
        delay

    member private self.ExecuteScriptAsync(scriptPath: string, isReload: bool, ct: CancellationToken) =
        let getFileInfo () =
            if File.Exists(scriptPath) then
                Some(FileInfo(scriptPath))
            else
                None

        let inline loadScript (fileInfo: FileInfo) (ct: CancellationToken) =
            task {
                logger.LogInformation("Loading script from {path}.", fileInfo.FullName)
                return! File.ReadAllTextAsync(fileInfo.FullName, ct)
            }

        let scriptOrDefault
            (defaultValue: {| Script: string
                              LastWriteTimeUtc: DateTime |} option)
            (fileInfo: FileInfo)
            (ct: CancellationToken)
            =
            task {
                match defaultValue with
                | None ->
                    let! text = loadScript fileInfo ct

                    return
                        {| Script = text
                           LastWriteTimeUtc = fileInfo.LastWriteTimeUtc |}
                | Some datum ->
                    if isReload
                       && fileInfo.LastWriteTimeUtc > datum.LastWriteTimeUtc then
                        let! text = loadScript fileInfo ct

                        return
                            {| Script = text
                               LastWriteTimeUtc = fileInfo.LastWriteTimeUtc |}
                    else
                        return datum
            }

        let mutable datum: {| Script: string
                              LastWriteTimeUtc: DateTime |} option =
            None

        getFileInfo ()
        |> Option.map (fun fileInfo ->
            task {
                let! res = scriptOrDefault datum fileInfo ct
                datum <- Some res
                return res.Script
            })
        |> Option.mapTask (fun script ->
            task {
                let parameters = Generic.Dictionary<string, obj>() :> IDictionary

                if logger.IsEnabled(LogLevel.Trace) then
                    logger.LogTrace("========================================")
                    logger.LogTrace("script:")
                    logger.LogTrace("{script}", script)
                    logger.LogTrace("========================================")

                do! self.runScript script parameters ct
            })
        |> ignore

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            let stopWatch = Stopwatch()
            // Environment.GetFolderPath(Environment.SpecialFolder.Startup)
            // AppDomain.CurrentDomain.BaseDirectory
            let scriptsPath = configuration["Worker:Scripts-Path"]
            stopWatch.Start()

            let mutable datum: {| Script: string
                                  LastWriteTimeUtc: DateTime |} option =
                None

            while not ct.IsCancellationRequested do
                logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)
                let scripts = Directory.GetFiles scriptsPath

                for scriptPath in scripts do
                    let isReload =
                        stopWatch.Elapsed.TotalSeconds
                        >= self.ScriptReloadInterval

                    if isReload then stopWatch.Restart()
                    self.ExecuteScriptAsync(scriptPath, isReload, ct)

                do! Task.Delay(self.Delay, ct)
        }
