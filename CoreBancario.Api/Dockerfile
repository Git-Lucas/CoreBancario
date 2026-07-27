# Contexto de build é a raiz do repositório: CoreBancario.Api referencia CoreBancario.Aplicacao e
# CoreBancario.Infraestrutura por ProjectReference, e todos os projetos compartilham
# Directory.Build.props / Directory.Packages.props na raiz.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Só os .csproj e os Directory.*.props primeiro: o restore fica em camada própria e não invalida
# a cada mudança de código-fonte.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY CoreBancario.Api/CoreBancario.Api.csproj CoreBancario.Api/
COPY CoreBancario.Aplicacao/CoreBancario.Aplicacao.csproj CoreBancario.Aplicacao/
COPY CoreBancario.Infraestrutura/CoreBancario.Infraestrutura.csproj CoreBancario.Infraestrutura/
COPY CoreBancario.Dominio/CoreBancario.Dominio.csproj CoreBancario.Dominio/
RUN dotnet restore CoreBancario.Api/CoreBancario.Api.csproj

COPY CoreBancario.Api/ CoreBancario.Api/
COPY CoreBancario.Aplicacao/ CoreBancario.Aplicacao/
COPY CoreBancario.Infraestrutura/ CoreBancario.Infraestrutura/
COPY CoreBancario.Dominio/ CoreBancario.Dominio/

RUN dotnet publish CoreBancario.Api/CoreBancario.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Usuário não-root já embutido na imagem oficial (D2.2 em design.md não detalha isto por ser
# padrão das imagens .NET 8+; $APP_UID é o identificador estável entre as variantes da imagem).
USER $APP_UID

# Forma exec: o processo dotnet fica em PID 1 e recebe SIGTERM diretamente do Kubernetes.
ENTRYPOINT ["dotnet", "CoreBancario.Api.dll"]
