# The container image for the ISP lookup app: everything needed to run it, including a copy of the dataset.
#
# In plain terms, here is what happens:
#   1. The app is built and published. The version passed in APP_VERSION is written to /version.txt, so whoever
#      deploys the image can tell which version is answering (local builds say "dev").
#   2. The public iptoasn.com dataset is downloaded, so the image can answer lookups as soon as it starts.
#   3. The final image holds only the published app, that dataset and two small scripts. It runs as an
#      unprivileged user on port 8080, refreshes the dataset daily (see scripts/docker-entrypoint.sh) and reports
#      its health at /healthz.
#
# Build and run:
#   docker build -t comparevpn-tools .
#   docker run -p 8080:8080 comparevpn-tools

# ============================
# Step 1: Build the app
# ============================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so this layer is reused until the project or package versions change.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY CompareVPN.ISPLookup/CompareVPN.ISPLookup.csproj CompareVPN.ISPLookup/
RUN dotnet restore CompareVPN.ISPLookup/CompareVPN.ISPLookup.csproj

COPY CompareVPN.ISPLookup/ CompareVPN.ISPLookup/

# APP_VERSION must not contain spaces: some build services pass it to Docker without quotes.
ARG APP_VERSION=dev
RUN echo "$APP_VERSION" > CompareVPN.ISPLookup/wwwroot/version.txt

RUN dotnet publish CompareVPN.ISPLookup/CompareVPN.ISPLookup.csproj -c Release --no-restore -o /app/publish

# ============================
# Step 2: Download the dataset
# ============================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dataset
COPY scripts/download-data.sh /scripts/
RUN bash /scripts/download-data.sh /dataset

# ============================
# Step 3: The final image
# ============================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# curl downloads the daily dataset and answers the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
COPY scripts/download-data.sh scripts/docker-entrypoint.sh ./scripts/

# The data folder belongs to the app's user, so the daily download can replace the file.
COPY --from=dataset --chown=app:app /dataset/ ./data/

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    Tools__IspChecker__DataFilePath=/app/data/ip2asn-combined.tsv.gz \
    DATASET_REFRESH_INTERVAL=24h

EXPOSE 8080

# "app" is the unprivileged user that comes with the .NET images.
USER app

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz > /dev/null || exit 1

ENTRYPOINT ["bash", "/app/scripts/docker-entrypoint.sh"]
