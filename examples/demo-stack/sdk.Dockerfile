FROM mcr.microsoft.com/dotnet/sdk:11.0

WORKDIR /workspace
COPY . .

RUN dotnet restore ImagingResults.sln \
        --source https://www.nuget.org/api/v2/ \
    && dotnet build ImagingResults.sln --configuration Release --no-restore

ENTRYPOINT ["bash", "/workspace/examples/demo-stack/run-artifacts.sh"]
