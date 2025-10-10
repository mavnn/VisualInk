{
  description = "Building a solid foundation with Falco and htmx";

  # This package automatically creates the inputs needed
  # for the `nugetDeps` field of the `buildDotnetModule`
  # helper we use below, based on the contents of the
  # `packages.lock.json` file
  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/release-25.05";
    nuget-packageslock2nix = {
      url = "github:mdarocha/nuget-packageslock2nix/main";
      inputs.nixpkgs.follows = "nixpkgs";
    };
  };

  outputs = { self, nixpkgs, flake-utils, nuget-packageslock2nix }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = import nixpkgs { system = system; };
        baseName = "VisualInk";
        version = "0.2";
        dnc = pkgs.dotnetCorePackages;
        # format-all = (import ./nix/format-all.nix) { inherit pkgs fantomas; };
        # format-stdin =
        #   (import ./nix/format-stdin.nix) { inherit pkgs fantomas; };
        local_postgres = (import ./nix/local_postgres.nix) { inherit pkgs; };
        server = (import ./nix/server.nix) {
          inherit pkgs dnc system baseName nuget-packageslock2nix;
        };
        # testExecutable = (import ./nix/server.test.nix) {
        #   inherit pkgs dnc system baseName server nuget-packageslock2nix;
        # };
      in rec {
        # Tools we want available during development
        devShells.default = pkgs.mkShell {
          buildInputs = [
            dnc.dotnet_9.sdk
            pkgs.nixfmt
            pkgs.skopeo
            pkgs.overmind
            pkgs.tmux
            pkgs.postgresql
            pkgs.fsautocomplete
            pkgs.fantomas
            # format-all
            # format-stdin
            local_postgres
          ];
        };

        # Default result of running `nix build` with this
        # flake; it builds the F# project `Server/VisualInk.fsproj`
        packages.default = server;

        # packages.testExecutable = testExecutable;

        # packages.test = pkgs.stdenv.mkDerivation {
        #   name = "${baseName}.TestResults";
        #   version = version;
        #   unpackPhase = "true";

        #   installPhase = ''
        #     ${testExecutable}/bin/VisualInk.Server.Test --junit-summary $out/server.test.junit.xml
        #   '';
        # };

        # A target that builds a fully self-contained docker
        # file with the project above
        packages.dockerImage = pkgs.dockerTools.buildImage {
          name = "${baseName}.Server";
          config = {
            # asp.net likes a writable /tmp directory
            Cmd = pkgs.writeShellScript "runServer" ''
              ${pkgs.coreutils}/bin/mkdir -p /tmp

              # Try a few times with a delay in
              # case postgres etc are starting up
              i=0
              until [ "$i" -ge 5 ]
              do
                ${packages.default}/bin/VisualInk.Server && break
                n=$((n+1))
                sleep 5
              done
            '';
            Env = [
              "DOTNET_EnableDiagnostics=0"
              "ASPNETCORE_URLS=http://+:5001"
              "SERILOG_JSON_LOGGING=true"
            ];
            ExposedPorts = { "5001/tcp" = { }; };
          };
        };
      });
}
