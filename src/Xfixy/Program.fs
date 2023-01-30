namespace Xfixy

open Xfixy.Services
open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.EventSource

module Program =
    let createHostBuilder args =
        Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(fun _hostContext services -> services.AddHostedService<Worker>() |> ignore)
            .ConfigureLogging(fun _hostingContext logging -> logging.AddEventSourceLogger() |> ignore)

    [<EntryPoint>]
    let main args =
        let host = createHostBuilder(args).Build()
        let configuration = host.Services.GetService<IConfiguration>()
        configuration["Worker:Scripts-Location"] <- Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Ps1-scripts")
        host.Run()

        0 // exit code
