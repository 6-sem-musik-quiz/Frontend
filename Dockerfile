FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so this layer is cached until a csproj changes
COPY global.json Directory.Build.props ./
COPY src/Frontend.Web/Frontend.Web.csproj src/Frontend.Web/
COPY src/Frontend.Services/Frontend.Services.csproj src/Frontend.Services/
COPY src/Frontend.Domain/Frontend.Domain.csproj src/Frontend.Domain/
RUN dotnet restore src/Frontend.Web/Frontend.Web.csproj

COPY src/ src/
RUN dotnet publish src/Frontend.Web/Frontend.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Frontend.Web.dll"]
