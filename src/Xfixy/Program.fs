namespace Xfixy

open System
open System.IO
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Configuration

module Program =
    let createHostBuilder args =
        Host
            .CreateDefaultBuilder(args)
            .ConfigureServices(fun hostContext services -> services.AddHostedService<Worker>() |> ignore)

    [<EntryPoint>]
    let main args =
        let host  = createHostBuilder(args).Build()
        let configuration = host.Services.GetService<IConfiguration>()
        configuration["Worker:Scripts-Path"] <- Path.Combine(Environment.CurrentDirectory, "ps1-scripts")
        host.Run()

        0 // exit code
