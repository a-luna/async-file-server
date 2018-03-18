dotnet restore
dotnet build

cd C:\Users\aaronluna\source\repos\tpl-sockets\TplSocketServerTools

# Instrument assemblies inside 'TplSocketServerTest' folder to detect hits for source files inside 'TplSocketServer' folder
dotnet minicover instrument --workdir ../ --assemblies TplSocketServerTest/**/bin/**/*.dll --sources TplSocketServer/**/*.cs 

# Reset hits count in case minicover was run for this project
dotnet minicover reset

cd ..

dotnet test --no-build TplSocketServerTest\TplSocketServerTest.csproj

cd TplSocketServerTools

# Uninstrument assemblies, it's important if you're going to publish or deploy build outputs
dotnet minicover uninstrument --workdir ../

# Create html reports inside folder coverage-html
dotnet minicover htmlreport --workdir ../ --threshold 90

# Print console report
# This command returns failure if the coverage is lower than the threshold
dotnet minicover report --workdir ../ --threshold 90

cd ..