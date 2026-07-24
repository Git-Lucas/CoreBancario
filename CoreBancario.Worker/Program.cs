using CoreBancario.Worker;

var construtor = Host.CreateApplicationBuilder(args);
construtor.Services.AddHostedService<Trabalhador>();

var anfitriao = construtor.Build();

await anfitriao.RunAsync();
