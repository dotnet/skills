FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
# Our image processing library depends on system zlib
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
COPY . .
RUN dotnet publish -c Release -o /app
FROM base
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ContainerApp.dll"]
