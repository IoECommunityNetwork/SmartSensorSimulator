# SmartSensor API Simulator module for Azure IoT Edge

> An Azure IoT Edge module to simulate SmartSensorAPI smart waste bin data.

Pre-requisites:
1. Follow this [tutorial](https://docs.microsoft.com/en-us/azure/iot-edge/tutorial-simulate-device-linux) to setup an IoT Edge runtime to deploy the module to, and
1. Follow this [tutorial](https://docs.microsoft.com/en-us/azure/iot-edge/tutorial-csharp-module) to setup your development environment so you can build the module

## What's Working

* D2C messages being sent to IoT Hub according to BinSensorReading data model object
    * A message is every 5 seconds
    * Uses a random number generator for fill level and temperature
    * Uses instantaneous time for timestamp

## What's Missing

* In addition to a random number generator, add other signal generators
* In addition to signal generators, let's call this synthetic mode, include 2 more modes:
    1. Read data from file captured from the actual SmartSensorAPI data, and
    1. Invoke the actual SmartSensorAPI
        * Would use module twin to pass configuration to the module at runtime
* Generalise the module into a class library using IoC/DI to create a Simulator Module framework
    * Could use module twin to decalre configuration for DI
    * Could consturct a basic simulated device with dimulated sensors without any coding, just configuration