#!/bin/bash

echo ""
echo "===================================================="
echo "  Generative Design Tool - Starting Server"
echo "===================================================="
echo ""

echo "Checking Python installation..."
if ! command -v python3 &> /dev/null; then
    echo "Error: Python 3 is not installed"
    exit 1
fi

python3 --version

echo ""
echo "Installing dependencies..."
pip3 install -r requirements.txt
if [ $? -ne 0 ]; then
    echo "Error: Failed to install dependencies"
    exit 1
fi

echo ""
echo "Starting server..."
echo ""
echo "The app will be available at:"
echo "  http://localhost:8000"
echo ""
echo "Press Ctrl+C to stop the server"
echo ""

python3 app.py

