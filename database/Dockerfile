# Database tools image: pgschema plus the PostgreSQL client, running as a normal user
# (pgschema starts a temporary PostgreSQL to validate plans, which refuses to run as root).
#   docker build -t pesaroll-db-tools database/
FROM postgres:18
ARG PGSCHEMA_VERSION=1.13.1
ARG TARGETARCH
ADD --chmod=755 https://github.com/pgplex/pgschema/releases/download/v${PGSCHEMA_VERSION}/pgschema-${PGSCHEMA_VERSION}-linux-${TARGETARCH} /usr/local/bin/pgschema
USER postgres
WORKDIR /db
ENTRYPOINT ["/db/db.sh"]
