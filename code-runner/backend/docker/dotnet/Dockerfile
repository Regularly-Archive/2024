FROM mcr.microsoft.com/dotnet/sdk:10.0-preview

# preview sdk may not have command adduser
RUN apt-get update && apt-get install -y adduser sudo

RUN dotnet workload update && dotnet tool install --tool-path /tools dotnet-script

RUN useradd -m -s /bin/bash sandbox \
    && adduser sandbox sudo \
    && chown -R sandbox:sandbox /tools

USER sandbox
WORKDIR /home/sandbox

ENV PATH="/tools:${PATH}"