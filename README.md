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

For example:
```
{
  "sesnorID": 99,
  "binID": 666,
  "binName": "Random Smart Sensor Simulator Module",
  "binCategory": "Smart Sensor Simulator ",
  "latitude": -33.869033,
  "longitude": 151.208895,
  "fillLevel": 56,
  "temperature": 33,
  "timestampdata": 1519097620
}
```

## What's Missing

* In addition to a random number generator, add other signal generators
* In addition to signal generators, let's call this synthetic mode, include 2 more modes:
    1. ~~Read data from file captured from the actual SmartSensorAPI data, and~~
        1. Currently just reading sample sensorData but dummy values for sensorDetail
            1. ~~Need to read sample sensorDetail~~
            1. Need to filter our -ve fillLevel values stemming from non-sensical ultrasound values
        1. Refactor the following configuration values into either a configuration file or module/device twin
            1. sensorId
            2. Message periodicity 
    1. Invoke the actual SmartSensorAPI
        * Would use module twin to pass configuration to the module at runtime
* Generalise the module into a class library using IoC/DI to create a Simulator Module plugin framework
    * Could use module twin to declare configuration for DI
    * Could consturct a basic simulated device with simulated sensors without any coding, just configuration