# Space Automation tasks. Install just: https://github.com/casey/just#installation

# List available tasks
default:
    @just --list

# Build every project
build:
    dotnet build backend/SpaceAutomation.slnx

# Run the test suite
test: build
    dotnet run --project backend/SpaceAutomation.Tests --no-build

# Launch the game; extra arguments pass through (e.g. just run --paused)
run *args:
    dotnet run --project backend/SpaceAutomation.Host -- {{args}}

# Remove build artifacts
clean:
    rm -rf backend/*/bin backend/*/obj
