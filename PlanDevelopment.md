Work with the database polygonselector and the schema example_polygon. 
Three tables are already created in the database with the following structure:
		* table name1: "deso_2025"
		* table name1: "tatorter_2023"
		* table name1: "buildings"
add to the schema and the tables to the appconfiguration file, and make sure the geometry column and attribute columns are correctly specified for each table.

develop the POST endpoint in the WebServerPolygonSelector that accepts a polygon as input, processes each of the configured tables, and sends updates to the client via SSE for each processed table, including the table title and matched features.
make the necessary database queries to retrieve the matching features based on the input polygon and the specified geometry column for each table.
develop the SSE endpoint to stream updates to the client as each table is processed, ensuring that the updates include the table title and the matched features in GeoJSON format.
Develop the client application in TestClientPolygonSelector that sends a POST request with a polygon to the server and listens for SSE updates, displaying the received data in a user-friendly format.
You are allowed to use any libraries or tools that you find necessary to implement the functionality, such as Npgsql for database interaction, GeoJSON.NET for handling GeoJSON data, and ASP.NET Core for building the web server and SSE endpoint.