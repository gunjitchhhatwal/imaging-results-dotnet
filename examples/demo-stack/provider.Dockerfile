FROM node:24.11.1-alpine AS build

RUN apk add --no-cache git

ARG MEDPLUM_PROVIDER_COMMIT=c16dc278caf21b41fefa3db0a3447e1a44e9aee7
ARG MEDPLUM_BASE_URL=http://localhost:8103/

WORKDIR /src
RUN git init \
    && git remote add origin https://github.com/medplum/medplum-provider.git \
    && git fetch --depth 1 origin "${MEDPLUM_PROVIDER_COMMIT}" \
    && git checkout --detach FETCH_HEAD
COPY examples/medplum/provider-package-lock.json /src/package-lock.json
RUN npm ci
ENV MEDPLUM_BASE_URL=${MEDPLUM_BASE_URL}
RUN npm run build

FROM nginx:1.27.5-alpine

COPY --from=build /src/dist/ /usr/share/nginx/html/
COPY --from=build /src/LICENSE.txt /usr/share/licenses/medplum-provider/LICENSE.txt
COPY examples/demo-stack/provider-nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
