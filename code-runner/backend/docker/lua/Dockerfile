FROM nickblah/lua

WORKDIR /home/sandbox

RUN adduser --disabled-password --gecos '' sandbox \
    && chown -R sandbox:sandbox /home/sandbox

USER sandbox
CMD ["lua"]
