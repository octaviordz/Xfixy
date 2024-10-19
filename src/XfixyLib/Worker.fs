namespace Xfixy.Services

open Xfixy
open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Configuration
open System.Collections
open Elmish

open Control

type Worker(logger: ILogger<Worker>, configuration: IConfiguration) =
    inherit BackgroundService()
    do setLogger logger

    override self.ExecuteAsync(ct: CancellationToken) =
        task {
            logger.LogInformation("Worker starting at: {time}.", DateTimeOffset.Now)

            let scriptReloadInterval () =
                let v = configuration.GetValue<int>("Worker:Script:ReloadIntervalSeconds", 15)
                v

            let delay () =
                let v = configuration.GetValue<int>("Worker:Delay", 4000)
                let v = Convert.ToInt64(v)
                TimeSpan(v * TimeSpan.TicksPerMillisecond)

            let scriptsPath () =
                let v = configuration.GetValue<string>("Worker:Scripts-Location", String.Empty)
                v

            let scriptsLocation = scriptsPath ()

            // Initial model and Cmd.
            let init (arg) : Model * Cmd<Msg> =
                let workerModel =
                    { LastFetchScriptsResult = FetchScriptsResult.None
                      ScriptDict = Generic.Dictionary<string, string>()
                      ScriptResultDict = HasNotStartedYet
                      ScriptsConfig = { Location = scriptsLocation }
                      CancellationToken = ct }

                let clientInit = Client.init {| CancellationToken = ct |}

                let clientModel, clientCmd = clientInit
                let msg = (FetchScripts Started) |> WorkerMsg

                let cmd =
                    Cmd.batch [ Cmd.ofMsg msg; (clientCmd |> Cmd.map (fun it -> it |> ClientMsg)) ]

                let model = (workerModel, clientModel)
                (model, cmd)

            let view _model _dispatch = ignore

            let subscriptionObservers = ResizeArray<IObserver<SubscriptionKind list>>()

            let subscriptionObservable =
                { new IObservable<SubscriptionKind list> with
                    member _.Subscribe(observer) =
                        if not (subscriptionObservers.Contains observer) then
                            subscriptionObservers.Add observer

                        new Unsubscriber<SubscriptionKind list>(subscriptionObservers, observer) }

            let triggerSubscription kindList =
                for observer in subscriptionObservers do
                    observer.OnNext(kindList)
            // https://elmish.github.io/elmish/docs/subscription.html#migrating-from-v3
            let subscription (model: Model) : (SubId * Subscribe<Msg>) list =
                let execSubscription dispatch : IDisposable =
                    subscriptionObservable
                    |> Observable.subscribe (fun kList ->
                        kList
                        |> List.map (fun it ->
                            match it with
                            | SubscriptionKind.FetchScripts -> WorkerMsg(FetchScripts Started)
                            | SubscriptionKind.ExecuteScripts -> WorkerMsg(ExecuteScripts Started))
                        |> List.iter (fun msg -> dispatch msg))

                [ [ nameof execSubscription ], execSubscription ]

            Program.mkProgram init update view
            |> Program.withErrorHandler (fun (error: string, ex: exn) -> logger.LogError(ex, error))
            |> Program.withSubscription subscription
            |> Program.run
            ///////////////////////////
            let stopWatch = Stopwatch()

            while not ct.IsCancellationRequested do
                let tms = delay ()
                do! Task.Delay(tms, ct)
                logger.LogInformation("Worker running at: {time}.", DateTimeOffset.Now)

                let fetchOrNot =
                    if not stopWatch.IsRunning then
                        stopWatch.Start()
                        Not
                    elif stopWatch.Elapsed.TotalSeconds >= scriptReloadInterval () then
                        Fetch
                    else
                        Not

                match fetchOrNot with
                | Fetch ->
                    stopWatch.Restart()

                    [ SubscriptionKind.FetchScripts; SubscriptionKind.ExecuteScripts ]
                    |> triggerSubscription
                | Not -> [ SubscriptionKind.ExecuteScripts ] |> triggerSubscription
        }
