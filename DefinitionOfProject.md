I want you to help me plan and implement this project.
- PostGIS: A powerful spatial database extender for PostgreSQL, allowing you to perform complex spatial queries and analyses.
- QGIS: A free and open-source geographic information system that enables you to create, edit, visualize, analyze, and publish geospatial information.
- GDAL: A translator library for raster and vector geospatial data formats, providing tools for data manipulation and analysis.
- Open layers: A web-based platform for sharing and visualizing geospatial data, allowing you to collaborate with others and access a wide range of datasets.
- Npgsql: A .NET data provider for PostgreSQL, enabling you to connect to and interact with PostGIS databases from your applications.
- NetTopologySuite: A .NET library for spatial data structures and algorithms, providing tools for working with geometric data in your applications.
- GeoJSON.NET: A .NET library for working with GeoJSON data, allowing you to easily serialize and deserialize geospatial data in the GeoJSON format.
- ASP.NET Core: A cross-platform, high-performance framework for building modern web applications and APIs, ideal for creating the main server for this project.


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
For development the server is in the WSL environment and has the connectionstring to the database configured in the appsettings.json file. (create the appsettings.json file if it doesn't exist).
To implement the project, we will follow these steps:
1. **Add testtables to the database**: Ensure that the necessary tables with the appropriate geometry and attribute columns are present in the PostGIS database for testing purposes.
2. **Define the Configuration File**: Create a configuration file (e.g., `config.json`) that lists the database tables, including their names, titles, geometry column names, and attribute column names.
3. **Set Up the .NET Web Server**: Create a new .NET Web API project for the `WebServerPolygonSelector`. Implement the POST endpoint that accepts a polygon and processes the configured tables.
4. **Implement Database Interaction**: Use Npgsql to connect to the PostGIS database and perform spatial queries based on the input polygon for each configured table.
5. **Implement SSE for Updates**: Set up Server-Sent Events (SSE) to send updates to the client for each processed table, including the table title and matched features.
6. **Implement OAuth Token Introspection**: Integrate OAuth Token Introspection to authorize the POST endpoint, ensuring that only authorized clients can access the service.
7. **Create the Test Client**: Develop a test client in C# that can send POST requests with a polygon to the server and listen for SSE updates, displaying the received data.
8. **Testing and Deployment**: Test the entire workflow to ensure that the server correctly processes the input polygon, interacts with the database, and sends updates to the client. Finally, prepare the application for deployment in Docker or on a Ubuntu server.
By following these steps, we can successfully implement the `WebServerPolygonSelector` and the `TestClientPolygonSelector`, allowing us to search through the configured database tables based on a polygon input and receive real-time updates on the results.