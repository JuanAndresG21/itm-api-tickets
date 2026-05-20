# Itm API Tickets - Guia de ejecucion local, Docker y Kubernetes

Este documento explica como levantar todo en Visual Studio Community, ejecutar las APIs juntas, y desplegar en Kubernetes con Docker Desktop.

## 1) Requisitos de instalacion

- Visual Studio Community 2022 con .NET 8 SDK.
- Workload de .NET MAUI (si vas a correr la app movil).
- Docker Desktop con Kubernetes habilitado.
- kubectl en PATH (Docker Desktop lo incluye si activas Kubernetes).
- k6 (opcional para pruebas de carga).
- Redis y RabbitMQ (pueden correr en Docker).

### Instalacion recomendada

- Docker Desktop: habilita Kubernetes en Settings > Kubernetes.
- k6 (Windows):
  - choco: `choco install k6`
  - winget: `winget install k6`

## 2) Variables y secretos requeridos

Estas variables son obligatorias para que todo funcione:

- Jwt__Key: clave JWT compartida entre Gateway y Booking
- Jwt__Issuer: Itm.Booking.Api
- RabbitMq__Host: URL AMQP
- ConnectionStrings__Redis: host Redis

Ejemplos (local):

```
Jwt__Key=dev-secret-123
Jwt__Issuer=Itm.Booking.Api
RabbitMq__Host=amqp://guest:guest@localhost:5672
ConnectionStrings__Redis=localhost:6379
```

Puedes ponerlas como variables de entorno del perfil en Visual Studio o en tu sistema.

### Donde ponerlas (recomendado)

Opcion A - Visual Studio Community (por proyecto)
1) Click derecho al proyecto > Properties.
2) Debug > Environment variables.
3) Agrega cada variable con su valor (una por linea).

Opcion B - launchSettings.json (por proyecto)
Archivo: Properties/launchSettings.json
Agrega el bloque "environmentVariables" dentro del perfil "http".

Ejemplo:
```
"environmentVariables": {
   "ASPNETCORE_ENVIRONMENT": "Development",
   "Jwt__Key": "dev-secret-123",
   "Jwt__Issuer": "Itm.Booking.Api",
   "RabbitMq__Host": "amqp://guest:guest@localhost:5672",
   "ConnectionStrings__Redis": "localhost:6379"
}
```

Opcion C - variables del sistema (para todos los proyectos)
- Windows (PowerShell):
```
$env:Jwt__Key = "dev-secret-123"
$env:Jwt__Issuer = "Itm.Booking.Api"
$env:RabbitMq__Host = "amqp://guest:guest@localhost:5672"
$env:ConnectionStrings__Redis = "localhost:6379"
```

En Kubernetes, los valores se definen en los YAML de deployment.

## 3) Levantar Redis y RabbitMQ (local)

Puedes usar Docker para levantar ambos servicios:

```
docker run -d --name itm-redis -p 6379:6379 redis:7

docker run -d --name itm-rabbitmq -p 5672:5672 -p 15672:15672 rabbitmq:3-management
```

RabbitMQ UI: http://localhost:15672 (user/pass: guest/guest)

## 4) Ejecutar las APIs en Visual Studio (multi start)

Solucion: Itm.Store.System.slnx

1) Abre la solucion en Visual Studio.
2) Click derecho en la solucion > Set Startup Projects.
3) Selecciona Multiple startup projects.
4) Marca Start para:
   - Itm.Gateway.Api
   - Itm.Booking.Api
   - Itm.Event.Api
   - Itm.Discount.Api
5) Ejecuta (F5).

URLs locales por defecto:
- Gateway: http://localhost:5183
- Booking: http://localhost:5148
- Event: http://localhost:5161
- Discount: http://localhost:5176

## 5) Flujo base a probar

1) Obtener JWT
   - POST http://localhost:5183/api/auth/token
   - Body JSON: { "username": "itm", "password": "2026" }

2) Comprar boleta (protegido)
   - POST http://localhost:5183/api/bookings/secure
   - Body JSON: { "eventId": 1, "tickets": 1, "discountCode": "ITM50" }
   - Header: Authorization: Bearer <token>

3) SignalR (notificacion)
   - Hub: http://localhost:5183/hubs/tickets
   - Evento: ticket-ready

## 6) App movil MAUI

Proyecto: Itm.Store.Mobile

- Base URL por defecto: https://api.itm-tickets.com
- Para entorno local puedes setear variable:
  - GATEWAY_URL=http://10.0.2.2:5183

o port forward
kubectl port-forward svc/gateway-api-service 5183:80

Pasos:
1) Iniciar sesion (obtiene JWT real de /api/auth/token)
2) Comprar boleta
3) Escuchar SignalR para confirmacion

## 7) Pruebas de carga (k6)

Script: test-load.js

- Usa BOOKING_URL si quieres apuntar al gateway:

```
set BOOKING_URL=http://localhost:5183/api/bookings
k6 run test-load.js
```

## 8) Docker build y push

Cada API tiene Dockerfile.
Ejemplo (Booking):

```
docker build -t juanandres0221/itm-booking-api:latest -f Itm.Booking.Api/Dockerfile .
docker push juanandres0221/itm-booking-api:latest

docker build -t juanandres0221/itm-discount-api:latest -f Itm.Discount.Api/Dockerfile .
docker push juanandres0221/itm-discount-api:latest

docker build -t juanandres0221/itm-event-api:latest -f Itm.Event.Api/Dockerfile .
docker push juanandres0221/itm-event-api:latest

docker build -t juanandres0221/itm-gateway-api:latest -f Itm.Gateway.Api/Dockerfile .
docker push juanandres0221/itm-gateway-api:latest
```

## 9) Kubernetes (Docker Desktop)

Manifests:
- gateway-deployment.yaml
- booking-deployment.yaml
- event-deployment.yaml
- discount-deployment.yaml
- itm-ingress.yaml
- *-hpa.yaml

Antes de aplicar:
- Reemplaza `your-dockerhub-user` en los YAMLs.
- Edita el archivo .env con los valores reales (JWT, RabbitMQ, Redis y URLs internas).

Aplicar (Kustomize usa .env para generar el Secret `itm-env`):

```
kubectl apply -k .
```

### Recompilar y reiniciar deployments

Si cambiaste codigo o imagenes Docker, debes hacer build/push de la imagen y luego reiniciar el deployment para que baje la nueva version.

```
kubectl rollout restart deployment/booking-api-deployment
kubectl rollout restart deployment/event-api-deployment
kubectl rollout restart deployment/discount-api-deployment
kubectl rollout restart deployment/gateway-api-deployment
```

Para esperar a que terminen de actualizarse:

```
kubectl rollout status deployment/booking-api-deployment
kubectl rollout status deployment/event-api-deployment
kubectl rollout status deployment/discount-api-deployment
kubectl rollout status deployment/gateway-api-deployment
```

### Ingress local

Si pruebas localmente con NGINX ingress:
- Agrega a hosts (C:\Windows\System32\drivers\etc):
  - 127.0.0.1 api.itm-tickets.com

### port forward para swagger con kubernete
kubectl port-forward svc/booking-api-service 5001:80
http://localhost:5001/swagger 

### Metrics Server (HPA)

```
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml
kubectl patch deployment metrics-server -n kube-system --type='json' -p='[{"op": "add", "path": "/spec/template/spec/containers/0/args/-", "value": "--kubelet-insecure-tls"}]'
```

## 10) CI/CD (GitHub Actions)

Workflow: .github/workflows/docker-publish.yml

Configura estos secretos en GitHub:
- DOCKERHUB_USERNAME
- DOCKERHUB_TOKEN

Al hacer push a main, se compilan y publican las imagenes.

---

Si quieres, puedo agregar un docker-compose para Redis/RabbitMQ o dejar scripts de PowerShell para setear env vars en Visual Studio.
