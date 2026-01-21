#!/usr/bin/env sh

output="./bin"
rm -rvf $output

for project in */*.csproj; do
    dotnet build $project -c release -o $output
done
