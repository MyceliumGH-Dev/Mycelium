@echo off
echo.
echo ====================================================
echo   Generative Design Tool - Starting Server
echo ====================================================
echo.
echo Checking Python installation...
python --version
if errorlevel 1 (
    echo Error: Python is not installed or not in PATH
    pause
    exit /b 1
)

echo.
echo Installing dependencies...
pip install -r requirements.txt
if errorlevel 1 (
    echo Error: Failed to install dependencies
    pause
    exit /b 1
)

echo.
echo Starting server...
echo.
echo The app will be available at:
echo   http://localhost:8000
echo.
echo Press Ctrl+C to stop the server
echo.
python app.py
pause

