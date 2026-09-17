# Notaker para Windows

Dictado local en español e inglés. Pulsa un atajo para grabar y vuelve a pulsarlo para transcribir y pegar el texto en la aplicación de origen.

## Uso

1. Abre `artifacts/Notaker/Notaker.exe` (o extrae el ZIP completo).
2. Selecciona **Large v3 Turbo** como punto de partida para mayor precisión o **Large v3** para el modelo completo. Base y Small siguen disponibles para equipos más lentos. Pulsa **Descargar modelo** una sola vez por modelo.
3. Elige el micrófono y el idioma. Automático reconoce el idioma dominante; fija Español o English para dictados en un solo idioma. **Español + English · Spanglish** añade contexto bilingüe sin solicitar traducción. No garantiza reconocer cada cambio de idioma dentro de una misma frase.
4. Sitúa el cursor en un campo de texto de cualquier aplicación normal.
5. Pulsa **Ctrl + Alt + Espacio**, habla y vuelve a pulsar el mismo atajo. El indicador inferior muestra grabación y procesamiento.
6. Notaker transcribe y envía **Ctrl+V** a la ventana de origen. El dictado también queda en el portapapeles. Si Windows impide activar la ventana o inyectar las teclas, pégalo manualmente con Ctrl+V.

Para cambiar el atajo, haz clic en el campo ATAJO GLOBAL, pulsa tu combinación y haz clic en Guardar. Esc cancela la captura y Tab sale del campo. Puedes combinar Ctrl, Alt, Shift o Win con otra tecla, o usar una tecla F (excepto F12). Si otra aplicación ocupa la combinación o Windows la reserva, Notaker avisa y conserva el atajo anterior. Cerrar la ventana la oculta; para terminar el proceso, usa **Salir** en el icono de la bandeja del sistema.

## Precisión y muletillas

Whisper.net ejecuta los modelos Whisper mediante whisper.cpp. No es un servicio de corrección editorial. Whisper puede omitir vacilaciones, pero **no garantiza eliminar muletillas, repeticiones ni corregir todos los errores**. Los modelos mayores suelen mejorar la precisión con más consumo de memoria y tiempo de procesamiento. Prueba los modelos con tu voz y vocabulario. Large v3 Turbo (8 bits, 834 MiB) y Large v3 completo (2,9 GiB) se ejecutan localmente: no añaden gasto de API, pero consumen más RAM y tiempo de CPU. Las instalaciones nuevas proponen Turbo; actualizar conserva el modelo elegido anteriormente.

En **Escritura con IA y vocabulario personal** puedes activar la limpieza con DeepSeek e introducir tu clave. El modelo de texto predeterminado es `deepseek-flash`, configurable. El flujo es Whisper → edición de texto → pegado. El editor recibe instrucciones de quitar muletillas, puntuar y formar párrafos o listas cuando corresponda, conservando idioma y significado. La corrección conserva explícitamente el spanglish, evita adivinar palabras ambiguas y elimina los caracteres Braille espurios antes y después de la IA. Mantiene una sola llamada al modelo configurado, sin razonamiento; el importe exacto depende de los tokens del dictado y la respuesta. Como cualquier LLM, puede equivocarse: la transcripción original permanece accesible desde **Corregir → Ver transcripción original**. Respuestas vacías, truncadas, errores de red y fallos de autenticación hacen que se conserve el texto original.

**Vocabulario personal:** añade hasta 100 nombres, palabras o expresiones (una por línea). Se utilizan como pistas tanto en Whisper como en el editor de texto. Whisper recibe hasta 600 caracteres de vocabulario para no saturar su contexto; pon los términos prioritarios primero.

**Aprendizaje:** al pulsar **Corregir** en el historial o **Corregir último dictado** en la bandeja, las palabras nuevas que introduces se incorporan al vocabulario, excluyendo un conjunto básico de palabras comunes. No reentrena el modelo: aprende un diccionario de pistas. Puedes desactivar este aprendizaje y revisar o borrar entradas. No detecta ediciones realizadas en otras aplicaciones. El reconocimiento de nombres sigue sin ser infalible.

Para activar la IA: añade la clave, pulsa **Probar conexión**, marca la limpieza y guarda. La prueba usa una frase sintética; no envía el historial. No se necesita API para dictar localmente ni para usar vocabulario.

## Datos y límites

- Audio en memoria durante el dictado; no se guarda como archivo ni se envía a servicios externos.
- Al activar la limpieza, el texto del dictado y el vocabulario se envían a DeepSeek, con el coste y las políticas de ese proveedor. La clave se cifra con DPAPI para la cuenta actual de Windows. Nunca se guarda en texto plano.
- Ajustes, modelos, vocabulario e historial: `%LOCALAPPDATA%\Notaker`.
- Historial local **sin cifrado**, hasta 200 dictados. Se puede desactivar o borrar desde la ventana. Desactivarlo no borra entradas anteriores.
- El último texto permanece en memoria y se puede copiar desde la bandeja incluso con el historial desactivado. Al salir de Notaker se pierde esa copia en memoria.
- El portapapeles se sustituye por el dictado. Si tienes sincronización de portapapeles de Windows activada, se aplica la configuración de Windows.
- Límite de 10 minutos por grabación. Grabaciones sin suficiente nivel de audio se descartan para reducir resultados inventados sobre silencio. El umbral de energía no es un detector neuronal de voz: ruido fuerte puede producir texto incorrecto y una voz muy baja puede descartarse.
- Windows puede impedir el pegado en aplicaciones elevadas, pantallas seguras o campos que bloqueen pegar. El envío de teclas no demuestra que la aplicación destino haya aceptado el texto.
- No se incluye inicio automático, firma de código, instalador, transcripción en vivo ni aprendizaje de correcciones externas.

## Requisitos

Windows 10/11 x64, micrófono y una CPU compatible con el runtime de Whisper distribuido (AVX, AVX2 y FMA). El ejecutable publicado incluye .NET 8. El runtime nativo puede requerir [Microsoft Visual C++ Redistributable 2022 x64](https://aka.ms/vs/17/release/vc_redist.x64.exe). No requiere Python. Solo la limpieza opcional requiere una clave de API. Desde 0.5 se incluye aceleración GPU mediante Vulkan, activada por defecto. Requiere una GPU y un controlador compatibles; el runtime CPU sigue incluido. Puedes desactivar **Acelerar con GPU compatible** para usar CPU. No hace falta instalar CUDA ni cambiar el modelo descargado. El primer dictado puede tardar más por la inicialización del motor; los posteriores reutilizan el modelo cargado.

## Compilar y publicar

Requiere SDK .NET 8:

```powershell
dotnet restore Notaker.sln --configfile NuGet.Config --packages .packages
dotnet build Notaker.sln -c Release --no-restore
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
```

Salida: `artifacts/Notaker/Notaker.exe` y `artifacts/Notaker-win-x64.zip`. El ejecutable incluye el runtime nativo y .NET; los extrae automáticamente al arrancar. El ZIP incluye además esta guía y las licencias.

## Verificación

Pruebas de persistencia, recuperación de JSON dañado, historial, preferencias, vocabulario, protección de la clave y respuestas de la API simuladas:

```powershell
dotnet run --project tests/Notaker.Tests -c Release --no-restore
```

Pruebas adicionales del motor nativo con modelo base y dos WAV PCM de 16 kHz mono (JFK público en inglés y voz sintética de Windows en español):

```powershell
dotnet run --project tests/Notaker.Tests -c Release --no-restore -- artifacts/qa/ggml-base.bin artifacts/qa/english.wav artifacts/qa/spanish.wav
```

Prueba de escritorio en una sesión Windows interactiva (abre un editor de prueba, verifica conflictos del atajo, foco del indicador y pegado Unicode con Ctrl+V):

```powershell
dotnet run --project tests/Notaker.Tests -c Release --no-restore -- --desktop
```

Diagnóstico del ejecutable publicado:

```powershell
artifacts/Notaker/Notaker.exe --smoke-test artifacts/qa/window.png
artifacts/Notaker/Notaker.exe --transcribe artifacts/qa/spanish.wav artifacts/qa/ggml-base.bin artifacts/qa/result.txt es
```

La prueba visual abre y cierra la ventana usando una carpeta de datos aislada junto al PNG. La prueba de transcripción procesa archivos; no activa el micrófono.

Antes de distribuir: probar voz real en los dos idiomas, inicio/parada por atajo en Bloc de notas y navegador, cambios de foco, desconexión del micrófono, atajo ocupado, cierre desde bandeja y aplicaciones elevadas. Las pruebas automatizadas no sustituyen esa verificación de extremo a extremo.

## Referencias

- [Whisper.net](https://github.com/sandrohanea/whisper.net)
- [Whisper: modelos y precisión](https://github.com/openai/whisper#available-models-and-languages)
- [Modelos GGML](https://huggingface.co/ggerganov/whisper.cpp)
- [NAudio](https://github.com/naudio/NAudio)

- [DeepSeek Chat Completions](https://api-docs.deepseek.com/api/create-chat-completion/)


## Actualizar desde Notaker (0.3+)

Abre **Buscar actualizaciones** en la barra lateral. Si hay una versión estable nueva, pulsa **Actualizar y reiniciar**. La aplicación descarga `Notaker.exe` desde las releases de `Legui92/Notaker`, verifica tamaño, SHA-256 publicado por GitHub y versión del archivo, cierra el proceso actual, reemplaza el ejecutable y lo vuelve a abrir. Si no confirma el inicio en 30 segundos, restaura el ejecutable anterior. Los datos de `%LOCALAPPDATA%\Notaker` se conservan. La carpeta que contiene el ejecutable debe permitir escritura.

Desde **0.6**, las actualizaciones son públicas y anónimas: no necesitas una cuenta, GitHub CLI ni un token. Notaker no lee credenciales de GitHub. El acceso antiguo guardado se elimina al cargar los ajustes, conservando la clave de DeepSeek. Si una versión anterior falla por un token obsoleto, abre una vez el EXE de la release 0.6 o posterior; los datos se conservan.

Para pasar desde 0.1/0.2 se necesita abrir el nuevo `Notaker.exe` una vez: esas versiones no tienen actualizador. No hay que extraer un ZIP. Cierra la versión anterior desde la bandeja, reemplaza el EXE y ábrelo. A partir de 0.3 se usa el botón interno.

### Publicar una versión futura

1. Incrementa `<Version>` en `src/Notaker/Notaker.csproj` y actualiza las notas en `docs/releases`.
2. Compila y verifica las pruebas. Ejecuta `scripts/test-update.ps1` sobre el ejecutable publicado para comprobar sustitución y recuperación con archivos aislados.
3. Crea un commit y sube la rama.
4. Ejecuta `./scripts/release.ps1 -NotesPath docs/releases/vX.Y.Z.md` con GitHub CLI autenticado y permiso de escritura.

La publicación crea una release estable y un tag apuntando al commit actual. No cambia ni fusiona `main`. Una rama subida sin release no activa actualizaciones. El script no reemplaza versiones existentes.

Las pruebas de integración del actualizador usan copias en `artifacts/qa`, no la aplicación del Escritorio. La comprobación de una release pública real se ejecuta con `dotnet run --project tests/Notaker.Tests -c Release -- --live-update artifacts/qa/live-download`, sin autenticación.


## Tiempo de respuesta (0.5+)

El historial muestra por separado **Voz** (carga del modelo y reconocimiento) e **IA** (limpieza en DeepSeek, si está activa). Estos tiempos no incluyen el pegado. La aceleración conserva el modelo, el contexto bilingüe y la búsqueda de haces; no añade llamadas a la API. Los resultados pueden variar ligeramente por la aritmética del backend.

Medición local con Ryzen 7 9800X3D, RTX 3050 8 GB y Turbo Q8, usando la misma muestra sintética bilingüe: CPU 22,0–22,4 s; Vulkan 7,6 s en la primera ejecución y 1,3 s en las dos siguientes. El texto coincidió. No incluye DeepSeek y no es una garantía para otros audios o equipos. Puede reproducirse con:

```powershell
dotnet run --project tests/Notaker.Tests -c Release -- --benchmark cpu MODELO.bin MUESTRA.wav mixed
dotnet run --project tests/Notaker.Tests -c Release -- --benchmark vulkan MODELO.bin MUESTRA.wav mixed
```