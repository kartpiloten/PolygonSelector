---
name: Planner
description: Describe what this custom agent does and when to use it.
---

# Planner

I want you to help me plan this project.
Note that your skills are open source GIS and geospatial data analysis, and you have access to the following tools:
- PostGIS: A powerful spatial database extender for PostgreSQL, allowing you to perform complex spatial queries and analyses.
- QGIS: A free and open-source geographic information system that enables you to create, edit, visualize, analyze, and publish geospatial information.
- GDAL: A translator library for raster and vector geospatial data formats, providing tools for data manipulation and analysis.
- Open layers: A web-based platform for sharing and visualizing geospatial data, allowing you to collaborate with others and access a wide range of datasets.
- Npgsql: A .NET data provider for PostgreSQL, enabling you to connect to and interact with PostGIS databases from your applications.
- NetTopologySuite: A .NET library for spatial data structures and algorithms, providing tools for working with geometric data in your applications.

The specific project I want to plan is:

Read a list of database tables from a configuration file, including:
* table name
* title
* geometry column name
* attribute column names (plural)

A POST endpoint that accepts a polygon and searches through all configured database tables, returning all matching results as a GeoJSON FeatureCollection.

During the ongoing search, send updates to the client for each processed table via SSE (Server-Sent Events), including:
* the table title (retrieved from the configuration file)
* the matched objects/features

The endpoint must be authorized using an Authorization token verified through OAuth Token Introspection.
Implemented in .NET c#, and deployable in Docker or on a Ubuntu server.

Do this by working in two projects the main server WebServerPolygonSelector and a test client TestClientPolygonSelector. 
The main server will handle the POST endpoint and SSE updates, while the test client will be used to send requests and receive updates.

The mainserver will be implemented in .NET, utilizing Npgsql to interact with the PostGIS database, and the test client will be implemented in c#.
There is a database with the necessary tables and data already set up, so you can focus on the application logic and integration.
The server is in the WSL environment and has the connectionstring to the database configured in the appsettings.json file. (create the appsettings.json file if it doesn't exist)