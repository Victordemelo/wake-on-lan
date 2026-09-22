FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /source
COPY src/api/RemoteWake.Api.csproj src/api/
RUN dotnet restore src/api/RemoteWake.Api.csproj
COPY src/api/ src/api/
RUN dotnet publish src/api/RemoteWake.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
WORKDIR /app
RUN apk add --no-cache krb5-libs \
    && addgroup -S remotewake \
    && adduser -S remotewake -G remotewake
COPY --from=build /app .
USER remotewake
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "RemoteWake.Api.dll"]
