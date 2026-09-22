FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY src/worker/RemoteWake.Worker.csproj src/worker/
RUN dotnet restore src/worker/RemoteWake.Worker.csproj
COPY src/worker/ src/worker/
RUN dotnet publish src/worker/RemoteWake.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
COPY --from=build /app .
RUN addgroup -S remotewake && adduser -S remotewake -G remotewake
USER remotewake
ENTRYPOINT ["dotnet", "RemoteWake.Worker.dll"]
