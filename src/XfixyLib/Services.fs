namespace Xfixy.Services

open Xfixy
open System
open System.Diagnostics
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.IO.Pipes
open System.Runtime.ExceptionServices
open System.Collections
open Elmish

module internal Control =
    open Xfixy.Control

    type ScriptsConfig = { Path: string }

    type Status =
        | None
        | Ok of string
        | Error of string

    type FetchScriptsResult =
        | None
        | Ok
        | Error of string

    type AsyncOperationStatus<'T> =
        | Started
        | Finished of 'T

    type Model =
        { StatusMessage: Status
          LastFetchScriptsResult: FetchScriptsResult
          ScriptDict: Generic.IDictionary<string, string>
          ScriptResultDict: Generic.IDictionary<string, Result<string, string>>
          ScriptsConfig: ScriptsConfig
          CancellationToken: CancellationToken }
        static member Empty =
            { StatusMessage = Status.None
              LastFetchScriptsResult = FetchScriptsResult.None
              ScriptDict = Generic.Dictionary<string, string>()
              ScriptResultDict = Generic.Dictionary<string, Result<string, string>>()
              ScriptsConfig = { Path = String.Empty }
              CancellationToken = Unchecked.defaultof<_> }

    type Msg =
        | FetchScripts of Location: string
        | FetchScriptsFailed of exn
        | FetchScriptsSuccess of ContentDict: Generic.IDictionary<string, string>
        | ExecuteScripts
        | ExecuteScriptsFailed of exn
        | ExecuteScriptsSuccess of ResultDict: Generic.IDictionary<string, Result<string, string>>
        | ConsumeScriptsCompleted

    type ExecutionContext =
        { Logger: ILogger
          Configuration: IConfiguration
          CancellationToken: CancellationToken }
        static member Empty =
            { Logger = Unchecked.defaultof<_>
              Configuration = Unchecked.defaultof<_>
              CancellationToken = Unchecked.defaultof<_> }

    type internal FetchScript =
        | Fetch
        | Not

    // https://stackoverflow.com/questions/7168801/how-to-use-reraise-in-async-workflows-in-f
    // https://github.com/fsharp/fslang-suggestions/issues/660
    let inline reraisePreserveStackTrace (ex: exn) =
        (ExceptionDispatchInfo.Capture ex).Throw()
        Unchecked.defaultof<_>

    let fetchScriptsAsync location ct =
        task {
            let scriptFiles = Directory.GetFiles(location, "*.ps1")
            let! res = PSscript.loadScriptsAsync scriptFiles ct
            return FetchScriptsSuccess res
        }

    let runScriptsAync scriptContentDict ct =
        task {
            let! resDict = runScriptAsync scriptContentDict ct
            return ExecuteScriptsSuccess resDict
        }

open Control
// https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// New-Service -Name Xfinixy -BinaryPathName C:\Users\...\Xfixy.exe
type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()

    let messager = Messager()

    member self.OnMessage
        with public get (): IObservable<string> = messager

    member _.Delay =
        let delay = configuration.GetValue<int>("Worker:Delay", 4000)
        delay

    member _.ScriptReloadInterval =
        let delay = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
        delay

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            let mutable _pipeClient =
                new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)

            let ctx =
                { Logger = logger
                  Configuration = configuration
                  CancellationToken = ct }

            ctx.Logger.LogInformation("Worker starting at: {time}.", DateTimeOffset.Now)

            let ct = ctx.CancellationToken

            let scriptReloadInterval () =
                let v = ctx.Configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
                v

            let delay () =
                let v = ctx.Configuration.GetValue<int>("Worker:Delay", 4000)
                v

            let scriptsPath () =
                let v = ctx.Configuration.GetValue<string>("Worker:Scripts-Path", String.Empty)
                v

            let scriptsLocation = scriptsPath ()
            //configuration.GetReloadToken().RegisterChangeCallback
            //return! executeUpdateAsync ctx
            //////////////////////////////////////////////////
            let shareResultAsync (ctx: ExecutionContext) (psResultAsString: string) ct =
                task {
                    let pipeClient = _pipeClient
                    messager.Push psResultAsString

                    if not pipeClient.IsConnected then
                        try
                            do! pipeClient.ConnectAsync(100, ct)
                        with
                        | :? TimeoutException as ex ->
                            ctx.Logger.LogTrace(ex, "[CLIENT] Timeout error.")
                            ()

                    if pipeClient.IsConnected then
                        try
                            let sw = new StreamWriter(pipeClient)
                            sw.AutoFlush <- true
                            // Send a 'message' and wait for client to receive it.
                            do! sw.WriteLineAsync(psResultAsString.AsMemory(), ct)
                            pipeClient.WaitForPipeDrain()
                        with
                        | :? IOException as ex ->
                            // Catch the IOException that is raised if the pipe is broken or disconnected.
                            ctx.Logger.LogWarning(
                                ex,
                                """[CLIENT] IOError calling sendAsync. with message "{message}".""",
                                psResultAsString
                            )
                            // If broken create a new pipe.
                            pipeClient.Close()
                            pipeClient.Dispose()
                            _pipeClient <- new NamedPipeClientStream(".", "Xfixy-pipe", PipeDirection.InOut)
                        | _ as ex ->
                            ctx.Logger.LogError(
                                ex,
                                """Error calling sendAsync. with message "{message}".""",
                                psResultAsString
                            )

                            reraisePreserveStackTrace ex
                }

            let consumeScriptsResultAsync (ctx: ExecutionContext) (model: Model) =
                task {
                    let scriptContentDict = model.ScriptResultDict
                    let ct = model.CancellationToken

                    for kv in scriptContentDict do
                        let res = scriptContentDict[kv.Key]

                        match res with
                        | Result.Ok psResultAsString ->
                            ctx.Logger.LogInformation("""PowerShell Result: "{psResultAsString}".""", psResultAsString)

                            try
                                do! shareResultAsync ctx psResultAsString ct
                            with
                            | _ ->
                                ctx.Logger.LogError(
                                    """Error calling shareResult with value: "{psResultAsString}".""",
                                    psResultAsString
                                )
                        | Result.Error err -> ctx.Logger.LogError(err)

                    return ConsumeScriptsCompleted
                }

            let update (msg: Msg) (model: Model) =
                match msg with
                | FetchScripts location ->
                    let scriptsPath = location
                    let ct = model.CancellationToken

                    if not (Directory.Exists scriptsPath) then
                        model, Cmd.ofMsg (FetchScriptsFailed(DirectoryNotFoundException(location)))
                    else
                        let fetch location = fetchScriptsAsync location ct
                        model, (Cmd.OfTask.either fetch location id FetchScriptsFailed)
                | FetchScriptsFailed ex ->
                    let model' =
                        { model with LastFetchScriptsResult = Error $"ScriptsPath not found: {ex.Message}" }

                    model', Cmd.none
                | FetchScriptsSuccess contentDict ->
                    let model' =
                        { model with
                            ScriptDict = contentDict
                            LastFetchScriptsResult = Ok }

                    model', Cmd.none
                | ExecuteScripts ->
                    let scriptContentDict = model.ScriptDict
                    let ct = model.CancellationToken
                    let execute scriptContentDict = runScriptsAync scriptContentDict ct
                    model, (Cmd.OfTask.either execute scriptContentDict id ExecuteScriptsFailed)
                | ExecuteScriptsFailed ex ->
                    let model' =
                        { model with StatusMessage = Status.Error $"Error executing script. {ex.Message}" }

                    model', Cmd.none
                | ExecuteScriptsSuccess scriptContentDict ->
                    let model' = { model with ScriptResultDict = scriptContentDict }

                    let consume msg =
                        let m = msg
                        consumeScriptsResultAsync ctx model'

                    model', Cmd.OfTask.perform consume () id
                | ConsumeScriptsCompleted -> model, Cmd.none
            //////////////////////////////////////////////////
            // Initial model and Cmd.
            let init (arg) =
                { StatusMessage = Status.None
                  LastFetchScriptsResult = None
                  ScriptDict = Generic.Dictionary<string, string>()
                  ScriptResultDict = Generic.Dictionary<string, Result<string, string>>()
                  ScriptsConfig = { Path = scriptsLocation }
                  CancellationToken = ct },
                Cmd.ofMsg (FetchScripts scriptsLocation)

            let view model dispatch = ignore

            let fetchScriptsObservers = Generic.List<IObserver<unit>>()

            let fetchScriptsSubscription () =
                for observer in fetchScriptsObservers do
                    observer.OnNext()

            let fetchScriptsObservable: IObservable<unit> =
                { new IObservable<unit> with
                    member _.Subscribe(observer: IObserver<unit>) =
                        if not (fetchScriptsObservers.Contains observer) then
                            fetchScriptsObservers.Add observer

                        new Unsubscriber<unit>(fetchScriptsObservers, observer) }

            let executeScriptsObservers = Generic.List<IObserver<unit>>()

            let executeScriptsSubscription () =
                for observer in executeScriptsObservers do
                    observer.OnNext()

            let executeScriptsObservable: IObservable<unit> =
                { new IObservable<unit> with
                    member _.Subscribe(observer: IObserver<unit>) =
                        if not (executeScriptsObservers.Contains observer) then
                            executeScriptsObservers.Add observer

                        new Unsubscriber<unit>(executeScriptsObservers, observer) }
            // https://elmish.github.io/elmish/docs/subscription.html#migrating-from-v3
            let subscription (model: Model) : (SubId * Subscribe<Msg>) list =
                let fetchScriptsSub dispatch : IDisposable =
                    fetchScriptsObservable
                    |> Observable.subscribe (fun _ ->
                        dispatch (FetchScripts model.ScriptsConfig.Path)
                        ())

                let executeScriptSub dispatch : IDisposable =
                    executeScriptsObservable
                    |> Observable.subscribe (fun _ ->
                        dispatch ExecuteScripts
                        ())

                [ [ "executeScripts" ], executeScriptSub
                  [ "fetchScripts" ], fetchScriptsSub ]

            Program.mkProgram init update view
            |> Program.withErrorHandler (fun (error: string, ex: exn) -> ctx.Logger.LogError(ex, error))
            |> Program.withSubscription subscription
            |> Program.run
            ///////////////////////////
            //return! executeUpdateAsync ctx
            // (shareResult: string -> CancellationToken -> Task<unit>)
            let stopWatch = Stopwatch()

            while not ct.IsCancellationRequested do
                do! Task.Delay(delay (), ct)
                ctx.Logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)

                let fetchOrNot =
                    if not stopWatch.IsRunning then
                        stopWatch.Start()
                        Not
                    elif stopWatch.Elapsed.TotalSeconds
                         >= scriptReloadInterval () then
                        Fetch
                    else
                        Not

                match fetchOrNot with
                | Fetch ->
                    stopWatch.Restart()
                    fetchScriptsSubscription ()
                    executeScriptsSubscription ()
                | Not -> executeScriptsSubscription ()
        }
