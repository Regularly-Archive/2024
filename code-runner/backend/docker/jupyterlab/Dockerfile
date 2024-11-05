FROM jupyter/datascience-notebook:latest
ARG DOTNET_VERSION=8.0

USER root
ENV PATH="/opt/conda/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin:${PATH}"
ENV DOTNET_INTERACTIVE_CLI_TELEMETRY_OPTOUT='true'
ENV DOTNET_INTERACTIVE_SKIP_FIRST_TIME_EXPERIENCE='true'


# 安装 .NET SDK
RUN apt-get update && \
    apt-get install -y wget gpg apt-transport-https && \
    . /etc/os-release && \
    wget -q https://packages.microsoft.com/config/ubuntu/${VERSION_ID}/packages-microsoft-prod.deb  -O packages-microsoft-prod.deb && \
    dpkg -i packages-microsoft-prod.deb && \
    rm packages-microsoft-prod.deb && \
    apt-get update && \
    apt-get install -y dotnet-sdk-${DOTNET_VERSION} && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*

ENV PATH="/usr/bin/dotnet:${PATH}"

# 安装 Microsoft.dotnet-interactive
RUN mkdir -p /root/.dotnet/tools && \
    #dotnet workload update && \
    dotnet tool install Microsoft.dotnet-interactive --tool-path /.dotnet/tools

ENV PATH="/.dotnet/tools:${PATH}"

# 安装 .NET Kernel
# 注意：nbconvert 唯一认可的目录
RUN dotnet interactive jupyter install --path /opt/conda/share/jupyter/kernels


# 安装 .NET Interactive & .NET Kernel
RUN mkdir -p /home/jovyan/.local/share/jupyter/runtime && \
    mkdir -p /home/jovyan/.local/share/jupyter/kernels && \
    sudo chown -R jovyan /home/jovyan && \
    sudo chown -R jovyan /opt/conda/share/jupyter/kernels && \
    sudo chown -R jovyan /.dotnet/tools && \
    sudo chown -R jovyan /usr/bin/dotnet && \
    sudo chmod -R 770 /home/jovyan

COPY convert.py /nbconvert/convert.py
RUN sudo chown -R jovyan /nbconvert

# 设置 JupyterLab 为默认启动界面
RUN pip install jupyterlab

# 设置工作目录和启动命令
USER jovyan
WORKDIR /home/jovyan

CMD ["start.sh", "jupyter", "lab", "--allow-root", "--IdentityProvider.token=''"]
