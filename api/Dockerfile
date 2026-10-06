# Builds the API or the worker. Build from the repo root:
#   docker build -f api/Dockerfile -t pesaroll-api .
#   docker build -f api/Dockerfile --build-arg PROJECT=Pesaroll.Worker -t pesaroll-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT=Pesaroll.Api
WORKDIR /src
COPY global.json ./
COPY api/ api/
RUN dotnet publish "api/src/${PROJECT}/${PROJECT}.csproj" -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG PROJECT=Pesaroll.Api
ENV APP_DLL=${PROJECT}.dll \
    ASPNETCORE_URLS=http://+:8080
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\""]
