# syntax=docker/dockerfile:1.7
# Multistage build of the API. Build context: repository root.
#   docker build -f src/PharmacyDelivery.Api/Dockerfile --build-arg APP_VERSION=1.0.0 -t pharmacy-delivery-api:1.0.0 .
#   docker build -f src/PharmacyDelivery.Api/Dockerfile --target test-results --output type=local,dest=test-results .   # test reports only
ARG DOTNET_VERSION=10.0

# 1) restore: only project files first, so the NuGet layer is cached until a csproj/props changes.
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS restore
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PharmacyDelivery.Common/PharmacyDelivery.Common.csproj src/PharmacyDelivery.Common/
COPY src/PharmacyDelivery.Domain/PharmacyDelivery.Domain.csproj src/PharmacyDelivery.Domain/
COPY src/PharmacyDelivery.Messaging/PharmacyDelivery.Messaging.csproj src/PharmacyDelivery.Messaging/
COPY src/PharmacyDelivery.Repository/PharmacyDelivery.Repository.csproj src/PharmacyDelivery.Repository/
COPY src/PharmacyDelivery.Services/PharmacyDelivery.Services.csproj src/PharmacyDelivery.Services/
COPY src/PharmacyDelivery.Infrastructure/PharmacyDelivery.Infrastructure.csproj src/PharmacyDelivery.Infrastructure/
COPY src/PharmacyDelivery.Api/PharmacyDelivery.Api.csproj src/PharmacyDelivery.Api/
COPY src/PharmacyDelivery.CourierSimulator/PharmacyDelivery.CourierSimulator.csproj src/PharmacyDelivery.CourierSimulator/
COPY src/PharmacyDelivery.Tests/PharmacyDelivery.Tests.csproj src/PharmacyDelivery.Tests/
RUN dotnet restore src/PharmacyDelivery.Tests/PharmacyDelivery.Tests.csproj

# 2) build
FROM restore AS build
ARG APP_VERSION=0.0.0-local
COPY src/ src/
RUN dotnet build src/PharmacyDelivery.Api/PharmacyDelivery.Api.csproj -c Release --no-restore -p:MinVerVersionOverride=${APP_VERSION}

# 3) test: unit + architecture + documentation tests (integration tests need a Docker engine and run in their own CI job).
#    The step itself never fails: it keeps the reports and the exit code, so CI can export the report also for a red run.
#    /testresults: junit.xml (GitLab artifacts:reports:junit), tests.trx, coverage.cobertura.xml, exit-code.
FROM build AS test
ARG APP_VERSION=0.0.0-local
# Documentation is checked against the code (configuration reference, test plan) — only this stage needs it.
COPY DESIGN.md ./
COPY docs/*.md docs/
RUN dotnet test src/PharmacyDelivery.Tests/PharmacyDelivery.Tests.csproj -c Release --no-restore --filter "TestCategory!=Integration" \
        -p:MinVerVersionOverride=${APP_VERSION} \
        --settings src/PharmacyDelivery.Tests/coverage.runsettings \
        --results-directory /testresults \
        --logger "junit;LogFilePath=/testresults/junit.xml;MethodFormat=Class;FailureBodyFormat=Verbose" \
        --logger "trx;LogFileName=tests.trx" \
        --collect "XPlat Code Coverage"; \
    echo $? > /testresults/exit-code; \
    find /testresults -mindepth 2 -name coverage.cobertura.xml -exec mv {} /testresults/ \; ; \
    find /testresults -mindepth 1 -type d -prune -exec rm -rf {} + ; \
    cat /testresults/exit-code

# 3a) test results only — export to the host:
#   docker build -f src/PharmacyDelivery.Api/Dockerfile --target test-results --output type=local,dest=test-results .
FROM scratch AS test-results
COPY --from=test /testresults/ /

# 4) publish — the image is not produced when a test failed.
FROM build AS publish
ARG APP_VERSION=0.0.0-local
COPY --from=test /testresults/exit-code /tmp/test-exit-code
RUN test "$(cat /tmp/test-exit-code)" = "0" || { echo "Tests failed - see the test-results target (junit.xml)"; exit 1; }
RUN dotnet publish src/PharmacyDelivery.Api/PharmacyDelivery.Api.csproj -c Release --no-build -o /app/publish -p:MinVerVersionOverride=${APP_VERSION} \
    && mkdir -p /app/publish/logs

# 5) runtime: chiseled (distroless, non-root). "extra" adds ICU and tzdata (America/New_York opening hours, NFKC).
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-noble-chiseled-extra AS runtime
ARG APP_VERSION=0.0.0-local
LABEL org.opencontainers.image.title="pharmacy-delivery-api" \
      org.opencontainers.image.version="${APP_VERSION}" \
      org.opencontainers.image.licenses="MIT"
WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=5 CMD ["dotnet", "PharmacyDelivery.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "PharmacyDelivery.Api.dll"]
