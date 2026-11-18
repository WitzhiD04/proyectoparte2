# Consola web: se compila con Node y se sirve con nginx, detrás del API Gateway.
FROM node:22-alpine AS compilacion
WORKDIR /fuente
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production

FROM nginx:1.29-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=compilacion /fuente/dist/consola-web/browser /usr/share/nginx/html
EXPOSE 80
