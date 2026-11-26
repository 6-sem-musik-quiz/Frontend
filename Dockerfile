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

# app.css and Frontend.Web.styles.css keep their names between deploys. Cloudflare tells browsers to
# keep them for 4 hours, so a browser could run new HTML with old CSS (unstyled new components).
# Put a hash of each file in its URL in index.html: a changed file is a new URL. The precompressed
# index.html.gz/.br are rebuilt, since nginx serves those first (gzip_static).
RUN cd /app/publish/wwwroot \
 && for f in app.css Frontend.Web.styles.css; do \
      h=$(sha256sum "$f" | cut -c1-12); \
      sed -i "s#href=\"$f\"#href=\"$f?v=$h\"#" index.html; \
    done \
 && grep -q 'styles.css?v=' index.html \
 && rm -f index.html.gz index.html.br \
 && gzip -9 -k index.html

# Blazor WebAssembly is only static files, so nginx serves them. No .NET runtime in this image.
# nginx-unprivileged runs as user 101 and listens on 8080.
FROM nginxinc/nginx-unprivileged:1.29-alpine AS final
COPY nginx/default.conf /etc/nginx/conf.d/default.conf
COPY nginx/api-404.conf /etc/nginx/api.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 8080
