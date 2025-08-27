@echo off
setlocal enabledelayedexpansion
echo 🎯 STRATEGIC DATA ACQUISITION
echo =============================
echo 📊 API Call Quota: 25
echo 🎯 Target: Latest missing years (2023-2025)
echo.

set API_KEY=OI8FMGPE6K95H697
set BASE_URL=https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=SPY&interval=5min&outputsize=full&apikey=%API_KEY%

if not exist "acquired_data" mkdir "acquired_data"

set /a call_count=0
set /a max_calls=25

echo 📥 Fetching recent months...
echo.

for %%m in (2024-08 2024-07 2024-06 2024-05 2024-04 2024-03 2024-02 2024-01 2023-12 2023-11 2023-10 2023-09 2023-08 2023-07 2023-06 2023-05 2023-04 2023-03 2023-02 2023-01 2022-12 2022-11 2022-10 2022-09 2022-08) do (
    if !call_count! geq %max_calls% (
        echo ⚠️  Reached API call limit
        goto summary
    )
    
    set /a call_count+=1
    echo 📥 Fetching %%m (Call !call_count!/25)
    
    curl "%BASE_URL%&month=%%m" -s -o "acquired_data\SPY_%%m_5min.json"
    
    if exist "acquired_data\SPY_%%m_5min.json" (
        echo ✅ %%m: Data acquired
    ) else (
        echo ❌ %%m: Failed
    )
    
    if !call_count! lss %max_calls% (
        echo    ⏳ Waiting 15 seconds...
        timeout /t 15 >nul
    )
)

:summary
echo.
echo 📊 ACQUISITION SUMMARY
echo ======================
echo 📞 API Calls Used: !call_count!/25
echo 📁 Data saved to: ./acquired_data/
echo.
echo 🎯 Strategic acquisition completed!