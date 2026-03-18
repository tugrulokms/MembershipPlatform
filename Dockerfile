FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .

RUN echo "=== /src/src/ contents ===" && ls /src/src/ && echo "=== Directory.Build.targets ===" && cat /src/src/Directory.Build.targets 2>/dev/null || echo "NOT FOUND"

RUN dotnet publish src/MembershipService.Api/MembershipService.Api.csproj \
    -c Release \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MembershipService.Api.dll"]
