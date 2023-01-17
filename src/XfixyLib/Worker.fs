namespace Xfixy

open System
open System.Collections
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration

type internal FetchScript =
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
        let delay = configuration.GetValue<int>("Worker:Delay", 4000)
        delay

    member _.ScriptReloadInterval =
        let delay = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
        delay

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            let stopWatch = Stopwatch()
            let mutable cached = Unchecked.defaultof<_>

            while not ct.IsCancellationRequested do
                logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)
                let scriptsPath = configuration["Worker:Scripts-Path"]

                if not (Directory.Exists scriptsPath) then
                    logger.LogWarning("ScriptsPath not found: {scriptsPath}.", scriptsPath)
                else
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
                                let scriptFiles = Directory.GetFiles(scriptsPath, "*.ps1")
                                let! res = PSscript.loadScriptsAsync scriptFiles ct
                                cached <- res
                                return res
                            | Not -> return cached
                        }

                    for kv in scriptContentDict do
                        let scriptContent = kv.Value
                        let name = IO.Path.GetFileName kv.Key
                        let parameters: IDictionary = Generic.Dictionary<string, obj>()
                        let! res = PSscript.runScriptAsync scriptContent parameters ct

                        match res with
                        | Result.Ok psDataCollection ->
                            for item in psDataCollection do
                                let psObjRes = item.BaseObject.ToString()
                                logger.LogInformation("PowerShell Result: {PSObject}", psObjRes)
                                messageTracker.Push psObjRes
                        | Result.Error ex ->
                            logger.LogError(
                                """Error running "{scriptPath}". Error: {errorType}.""",
                                kv.Key,
                                ex.GetType()
                            )

                            if logger.IsEnabled(LogLevel.Trace) then
                                logger.LogTrace(
                                    """FileName: "{fileName}". ErrorType: {errorType} Error: {error}.""",
                                    name,
                                    ex.GetType(),
                                    ex
                                )

                do! Task.Delay(self.Delay, ct)
        }
