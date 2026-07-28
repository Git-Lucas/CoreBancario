# Contexto de build é a raiz do repositório — mesma razão do Dockerfile da Api.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY CoreBancario.Worker/CoreBancario.Worker.csproj CoreBancario.Worker/
COPY CoreBancario.Aplicacao/CoreBancario.Aplicacao.csproj CoreBancario.Aplicacao/
COPY CoreBancario.Infraestrutura/CoreBancario.Infraestrutura.csproj CoreBancario.Infraestrutura/
COPY CoreBancario.Dominio/CoreBancario.Dominio.csproj CoreBancario.Dominio/
RUN dotnet restore CoreBancario.Worker/CoreBancario.Worker.csproj

COPY CoreBancario.Worker/ CoreBancario.Worker/
COPY CoreBancario.Aplicacao/ CoreBancario.Aplicacao/
COPY CoreBancario.Infraestrutura/ CoreBancario.Infraestrutura/
COPY CoreBancario.Dominio/ CoreBancario.Dominio/

RUN dotnet publish CoreBancario.Worker/CoreBancario.Worker.csproj -c Release --no-restore -o /app/publish

# runtime, não aspnet: o Worker não expõe superfície HTTP, e nenhuma dependência transitiva
# exige o runtime ASP.NET.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

USER $APP_UID

ENTRYPOINT ["dotnet", "CoreBancario.Worker.dll"]
