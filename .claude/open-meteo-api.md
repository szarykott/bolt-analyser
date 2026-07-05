# Open-Meteo Historical Weather API

Free, no API key required (non-commercial).

## Base URL

```
https://archive-api.open-meteo.com/v1/archive
```

## Key Params

| Param | Description |
|---|---|
| `latitude` | GPS lat |
| `longitude` | GPS lon |
| `start_date` | YYYY-MM-DD |
| `end_date` | YYYY-MM-DD |
| `hourly` | comma-separated variables |

## Useful Variables

| Variable | Unit | Notes |
|---|---|---|
| `temperature_2m` | °C | air temp at 2m |
| `precipitation` | mm | total (rain + snow + sleet) |
| `rain` | mm | rain only |
| `snowfall` | cm | snowfall |
| `snow_depth` | m | snow on ground |
| `weather_code` | WMO code | type of weather event |

## Example

```
https://archive-api.open-meteo.com/v1/archive?latitude=50.06&longitude=19.94&start_date=2023-01-01&end_date=2023-01-31&hourly=temperature_2m,rain,snowfall,precipitation,weather_code
```

## Data Coverage

- Historical data back to 1940 (ERA5 reanalysis)
- Global coverage
- Hourly resolution

## Docs

https://open-meteo.com/en/docs/historical-weather-api