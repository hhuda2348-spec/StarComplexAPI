FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# نسخ ملف المشروع من المجلد الفرعي وبنائه
COPY ["StarComplexAPI/StarComplexAPI.csproj", "StarComplexAPI/"]
RUN dotnet restore "StarComplexAPI/StarComplexAPI.csproj"

COPY . .
WORKDIR "/src/StarComplexAPI"
RUN dotnet publish "StarComplexAPI.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StarComplexAPI.dll"]
