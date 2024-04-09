ARG PG_VERSION=15
FROM postgres:$PG_VERSION
ARG PG_VERSION

ENV DEBIAN_FRONTEND=noninteractive

# install dependency
RUN apt-get update && apt-get install -y --no-install-recommends \
    build-essential \
    cmake \
    gcc \
    g++ \
    libc6-dev \
    libpq-dev \
    postgresql-server-dev-$PG_VERSION \
    git \
    && rm -rf /var/lib/apt/lists/*

# configure git
RUN git config --global http.sslverify false && \
    git config --global httpS.sslverify false && \
    export GIT_SSL_NO_VERIFY=1

# install pg_jieba
RUN git clone https://github.com/jaiminpan/pg_jieba /usr/src/pg_jieba && \
    cd /usr/src/pg_jieba && \
    git submodule update --init --recursive && \
    mkdir build && cd build && cmake -DPostgreSQL_TYPE_INCLUDE_DIR=/usr/include/postgresql/$PG_VERSION/server -DCMAKE_CXX_FLAGS="-Wall -std=c++11" .. && \
    make && \
    make install && \
    cp /usr/src/pg_jieba/pg_jieba.control /usr/share/postgresql/$PG_VERSION/extension/ && \    
    cp /usr/src/pg_jieba/*.sql /usr/share/postgresql/$PG_VERSION/tsearch_data/ 
    #cp /usr/src/pg_jieba/jieba/dict/* /usr/share/postgresql/$PG_VERSION/tsearch_data/

# install pgvector
RUN git clone https://github.com/pgvector/pgvector.git /usr/src/pgvector && \
    cd /usr/src/pgvector && \
    make && \
    make install

# cleanup
RUN rm -rf /usr/src/pg_jieba && \
    rm -rf /usr/src/pgvector && \
    apt-get purge -y build-essential cmake gcc g++ && \
    apt-get autoremove -y

# load libraries
RUN echo "shared_preload_libraries = 'pg_jieba,pgvector'" >> /usr/share/postgresql/$PG_VERSION/postgresql.conf
CMD ["postgres"]