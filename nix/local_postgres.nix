{ pkgs }:
pkgs.writeShellScriptBin "local_postgres" ''
  [ ! -d ./local_services/pg ] \
    && echo "Creating local postgres files" \
    && mkdir -p local_services/pg \
    && ${pkgs.postgresql}/bin/initdb -D local_services/pg --username=postgres
  ${pkgs.postgresql}/bin/postgres -D local_services/pg -p "$PGPORT" -h "$PGHOST"
''
