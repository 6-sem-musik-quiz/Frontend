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

# Blazor WebAssembly is only static files, so nginx serves them. No .NET runtime in this image.
# nginx-unprivileged runs as user 101 and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 8080
