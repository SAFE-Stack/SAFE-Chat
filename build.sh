#!/bin/bash

# SAFE-Chat Build Script
# Runs the F# build script with dotnet fsi and passes through all arguments

dotnet fsi build.fsx -- -- "$@"
