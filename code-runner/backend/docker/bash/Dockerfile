FROM alpine:3.19

# Install bash and common utilities
RUN apk add --no-cache \
    bash \
    curl \
    wget \
    git \
    vim \
    nano \
    jq \
    python3 \
    py3-pip \
    nodejs \
    npm \
    make \
    gcc \
    g++ \
    libc-dev \
    linux-headers \
    && rm -rf /var/cache/apk/*

# Create sandbox user
RUN adduser -D -s /bin/bash sandbox \
  && addgroup sandbox wheel

# Set up bash configuration
RUN echo 'export PS1="\u@\h:\w\$ "' >> /home/sandbox/.bashrc \
  && echo 'export PATH=$PATH:/home/sandbox/.local/bin' >> /home/sandbox/.bashrc \
  && echo 'alias ll="ls -la"' >> /home/sandbox/.bashrc

USER sandbox
WORKDIR /home/sandbox

# Create common directories
RUN mkdir -p /home/sandbox/{bin,lib,src,tmp}

# Set default command
CMD ["/bin/bash"]