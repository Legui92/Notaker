# Seguridad y datos personales

Notaker es una aplicación de escritorio. El repositorio y las releases contienen código y dependencias, no claves de API de los usuarios.

- La clave de IA se introduce en cada instalación y se protege con Windows DPAPI para el usuario actual. No se incluye al compilar.
- Ajustes, vocabulario e historial se almacenan en `%LOCALAPPDATA%\Notaker`. El historial no está cifrado; puede desactivarse desde la aplicación.
- El audio se procesa localmente. La limpieza opcional envía el texto y el vocabulario al proveedor de IA configurado en la aplicación (DeepSeek).
- Desde 0.6, el actualizador consulta GitHub sin autenticación. No lee tokens ni utiliza sesiones de GitHub CLI.
- El actualizador valida HTTPS, repositorio, tamaño, huella SHA-256 y versión. Estas medidas no sustituyen la seguridad de la cuenta que publica las releases. Los ejecutables actualmente no tienen firma de código de Windows.

No publiques claves, archivos de configuración personales, historial de dictados, tokens, volcados de memoria ni logs con credenciales en issues o pull requests. Si una clave se publica, revócala en su proveedor: eliminar el archivo no invalida las copias ni el historial.

## Revisión del 17 de septiembre de 2026

Se revisaron ambas ramas públicas, todos los commits alcanzables (72 blobs Git en ese momento), textos de issues/PR/reviews/releases y los contenidos de las releases 0.3.0, 0.4.0 y 0.5.0. Gitleaks no detectó secretos. Se extrajeron las aplicaciones de los EXE empaquetados y se revisaron también sus cadenas. No había ejecuciones ni artefactos de GitHub Actions, wiki ni GitHub Pages publicados.

También se contrastó en memoria la credencial de IA configurada localmente y su representación cifrada con el historial y los textos/cadenas publicados: no se encontraron coincidencias. No se incluyeron esas credenciales en comandos, informes ni archivos de auditoría.

Se activaron el escaneo de secretos, la protección de push y las alertas de dependencias de GitHub. `.gitignore` excluye datos personales y archivos habituales de credenciales. La revisión no certifica ausencia total de vulnerabilidades ni garantiza que detecte cualquier secreto futuro. Como en cualquier repositorio público, código, historial y metadatos de autor de los commits son visibles.