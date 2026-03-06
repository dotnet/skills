FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG COPILOT_SDK_VERSION
RUN dotnet new console -o /tmp/dl \
    && dotnet add /tmp/dl package GitHub.Copilot.SDK --version $COPILOT_SDK_VERSION \
    && dotnet build /tmp/dl -c Release \
    && cp /tmp/dl/bin/Release/net10.0/runtimes/*/native/copilot /usr/local/bin/copilot \
    && chmod +x /usr/local/bin/copilot \
    && rm -rf /tmp/dl

RUN copilot --version