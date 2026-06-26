# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["GarionX.csproj", "./"]
RUN dotnet restore "GarionX.csproj"

# Copy the rest of the files and build the app
COPY . .
RUN dotnet publish "GarionX.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Copy compiled files from build stage
COPY --from=build /app/publish .

# Create directory for avatar uploads
RUN mkdir -p wwwroot/uploads

# Entrypoint to run the ASP.NET Core API
ENTRYPOINT ["dotnet", "GarionX.dll"]
