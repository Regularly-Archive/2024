FROM alpine:3.20

RUN sed -i 's/dl-cdn.alpinelinux.org/mirrors.tuna.tsinghua.edu.cn/g' /etc/apk/repositories \
 && apk update

RUN apk add --no-cache \
    bash curl wget git vim nano jq \
    python3 py3-pip \
    nodejs npm \
    make gcc g++ libc-dev linux-headers

RUN adduser -D -s /bin/bash sandbox

RUN echo 'export PS1="\u@\h:\w\$ "' >> /home/sandbox/.bashrc \
 && echo 'export PATH=$PATH:/home/sandbox/.local/bin' >> /home/sandbox/.bashrc \
 && echo 'alias ll="ls -la"' >> /home/sandbox/.bashrc

USER sandbox
WORKDIR /home/sandbox

RUN mkdir -p bin lib src tmp

CMD ["/bin/bash"]
