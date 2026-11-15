# Multi-stage Dockerfile for Frontend (Angular SPA)
# Stage 1: Build Angular application
FROM node:22-alpine AS build
WORKDIR /app

# Copy dependency manifests and install packages
COPY package*.json ./
RUN npm install

# Copy source files and build production bundle
COPY . .
RUN npm run build -- --configuration production

# Stage 2: Serve application using lightweight Nginx Alpine
FROM nginx:alpine AS runtime
WORKDIR /usr/share/nginx/html

# Clean default nginx files
RUN rm -rf ./*

# Copy built artifacts from build stage
COPY --from=build /app/dist/*/browser ./

# Copy custom Nginx configuration supporting SPA client-side routing
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
