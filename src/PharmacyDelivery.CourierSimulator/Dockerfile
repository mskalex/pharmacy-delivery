# syntax=docker/dockerfile:1.7
# Courier simulator (demo of real-time tracking). Build context: repository root.
ARG DOTNET_VERSION=10.0

FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PharmacyDelivery.Common/PharmacyDelivery.Common.csproj src/PharmacyDelivery.Common/
COPY src/PharmacyDelivery.CourierSimulator/PharmacyDelivery.CourierSimulator.csproj src/PharmacyDelivery.CourierSimulator/
RUN dotnet restore src/PharmacyDelivery.CourierSimulator/PharmacyDelivery.CourierSimulator.csproj

FROM restore AS publish
ARG APP_VERSION=0.0.0-local
COPY src/PharmacyDelivery.Common/ src/PharmacyDelivery.Common/
COPY src/PharmacyDelivery.CourierSimulator/ src/PharmacyDelivery.CourierSimulator/
RUN dotnet publish src/PharmacyDelivery.CourierSimulator/PharmacyDelivery.CourierSimulator.csproj -c Release --no-restore -o /app/publish -p:MinVerVersionOverride=${APP_VERSION}

FROM mcr.microsoft.com/dotnet/runtime:${DOTNET_VERSION}-noble-chiseled-extra AS runtime
ARG APP_VERSION=0.0.0-local
LABEL org.opencontainers.image.title="pharmacy-delivery-courier-simulator" \
      org.opencontainers.image.version="${APP_VERSION}"
WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "PharmacyDelivery.CourierSimulator.dll"]
