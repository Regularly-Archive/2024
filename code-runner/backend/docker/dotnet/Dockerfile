FROM mcr.microsoft.com/dotnet/sdk:8.0

RUN dotnet workload update && dotnet tool install --tool-path /tools dotnet-script

RUN useradd -m -s /bin/bash sandbox \
    && adduser sandbox sudo \
    && chown -R sandbox:sandbox /tools

USER sandbox
WORKDIR /home/sandbox

ENV PATH="/tools:${PATH}"