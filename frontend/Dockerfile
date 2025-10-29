# ====== BUILD ======
FROM node:24-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

# ====== SERVE ======
FROM nginx:alpine
# copia i file generati da Angular nella webroot di nginx
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
# copia il file di health (se lo tieni in src)
COPY ./src/health.json /usr/share/nginx/html/health.json
# copia la config di nginx
COPY ./nginx/default.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
