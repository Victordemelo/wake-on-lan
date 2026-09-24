# Compile on the build machine's architecture and target the image's one (amd64/arm64).
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /source
COPY src/shared/RemoteWake.Shared.csproj src/shared/
COPY src/worker/RemoteWake.Worker.csproj src/worker/
RUN dotnet restore src/worker/RemoteWake.Worker.csproj -a $TARGETARCH
COPY src/shared/ src/shared/
COPY src/worker/ src/worker/
RUN dotnet publish src/worker/RemoteWake.Worker.csproj -c Release -a $TARGETARCH -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine
WORKDIR /app
COPY --from=build /app .
RUN addgroup -S remotewake && adduser -S remotewake -G remotewake
USER remotewake
ENTRYPOINT ["dotnet", "RemoteWake.Worker.dll"]
