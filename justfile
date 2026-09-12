# Space Automation tasks. Install just: https://github.com/casey/just#installation

# List available tasks
default:
    @just --list

# Build every project
build:
    dotnet build backend/SpaceAutomation.sln

# Open the particle window and start the simulation server; extra arguments pass through (e.g. just run --paused)
run *args:
    @dotnet run --project backend/SpaceAutomation.Server -- {{args}}

# Run the reference player client against a running server
client:
    python3 player/main.py

# Remove build artifacts
clean:
    rm -rf backend/*/bin backend/*/obj
