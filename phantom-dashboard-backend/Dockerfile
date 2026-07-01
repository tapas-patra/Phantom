FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . ./
RUN dotnet publish Phantom.Dashboard.Backend.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

CMD ["sh", "-c", "dotnet Phantom.Dashboard.Backend.dll --urls http://0.0.0.0:${PORT:-10000}"]
