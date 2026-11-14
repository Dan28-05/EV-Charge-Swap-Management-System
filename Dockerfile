FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["EV Charge & Swap Management System/EV Charge & Swap Management System.csproj", "EV Charge & Swap Management System/"]
RUN dotnet restore "EV Charge & Swap Management System/EV Charge & Swap Management System.csproj"

COPY ["EV Charge & Swap Management System/", "EV Charge & Swap Management System/"]
RUN dotnet publish "EV Charge & Swap Management System/EV Charge & Swap Management System.csproj" \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# Local Compose runs this job before starting the API.
FROM build AS migration
RUN dotnet tool install dotnet-ef --tool-path /tools --version 9.0.15
ENTRYPOINT ["/tools/dotnet-ef", "database", "update", "--no-build", "--configuration", "Release", "--project", "/src/EV Charge & Swap Management System/EV Charge & Swap Management System.csproj", "--startup-project", "/src/EV Charge & Swap Management System/EV Charge & Swap Management System.csproj"]

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "EV Charge & Swap Management System.dll"]
