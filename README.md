# Job Consumer Sample

Use the `docker-compose.yml` file to startup RabbitMQ and Postgres, then run the service. Navigate to `https://localhost:5001/swagger` to post a video to convert. Enjoy the show!

Use Postman to send this request `GET http://localhost:5000/ConvertVideo/job/1` to test the job consumer.