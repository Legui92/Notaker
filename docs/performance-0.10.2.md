# Evaluación de rendimiento 0.10.2

## Aceptación

Revertir únicamente el límite adaptativo de salida de 0.10.1. Evaluar una aceleración local sin reducir contexto, precisión ni parámetros de reconocimiento, y sin añadir solicitudes a DeepSeek. Rechazar candidatos que no superen las pruebas de calidad. Publicar la corrección con mediciones útiles para continuar el diagnóstico.

## Límite de DeepSeek

La investigación previa comparó los mismos tres textos sintéticos con 8192 tokens y el límite adaptativo, cuatro pares alternados por longitud (24 solicitudes). Las medianas fueron 1.185/1.189 s (corto), 1.848/1.944 s (medio) y 3.521/3.515 s (largo válido). En largo solo 3/4 y 2/4 respuestas fueron válidas; las demás omitieron demasiado contenido repetido. No hay una mejora práctica demostrada que justifique el límite adaptativo. Se restaura 8192, conservando el rechazo de respuestas incompletas y las protecciones de tiempo.

## Flash Attention: candidato rechazado

Whisper.net 1.9.0 ofrece `WhisperFactoryOptions.UseFlashAttention`, desactivado por defecto. Se probó Turbo q8 con Vulkan en este PC, mismo idioma automático, prompt bilingüe, temperatura cero y beam search. No se modificó el motor de producción.

`Notaker.Tests --attention-benchmark MODEL FIXTURE_FOLDER OUTPUT_JSON` ejecuta tres rondas con orden AB/BA/AB. Requiere WAV mono 16 kHz en english.wav, spanish.wav y mixed.wav. El fixture mixed debe contener exactamente: «Necesito revisar el pull request antes de hacer el deployment mañana. Comprueba que pasen las pruebas del proyecto.» La prueba larga repite ese audio completo, con un segundo de silencio entre repeticiones, hasta alcanzar 120 segundos. Esta ejecución tuvo diez repeticiones. La carga del modelo se fuerza antes del cronómetro. La primera inferencia puede incluir inicialización de kernels; no se afirma un benchmark universal de GPU caliente.

| Audio | Mediana estándar | Mediana Flash | Comparación de texto |
|---|---:|---:|---|
| Inglés | 1.229 s | 0.658 s | Idéntico entre variantes |
| Español | 1.263 s | 0.678 s | Idéntico entre variantes |
| Spanglish | 1.264 s | 0.717 s | Idéntico entre variantes |
| Largo repetido | 4.655 s | 3.042 s | 25 frente a 31 ediciones contra referencia |

Son tres muestras por variante, no intervalos de confianza. El audio repetitivo ya genera errores con el motor estándar y no representa toda el habla natural. Aun así, el aumento reproducido de errores impide aceptar la activación global bajo el requisito de conservar precisión. Reutilizar `WhisperProcessor` tampoco evita la creación/liberación de estado nativo por llamada en la versión instalada; no se implementó ese candidato.

## Mejora de diagnóstico implementada

Los registros rotativos existentes añaden `elapsedMs`, `succeeded` y `responseHeadersMs` cuando corresponda. Etapas `model.load.gpu/cpu.timing`, `recognition.gpu/cpu.timing` y `polish.timing`. La inferencia excluye la carga del modelo; cada fragmento en vivo tiene su propia medición. La limpieza registra éxito, error o cancelación y separa la llegada de cabeceras del total. Las cabeceras de una respuesta no streaming no indican el primer token ni permiten separar red de cómputo del proveedor. No se registran texto, audio, claves ni rutas del modelo.

La mejora observable es poder distinguir esas etapas incluso ante fallos; no se atribuye una aceleración a la instrumentación. La transcripción completa de recuperación y una llamada final de IA se mantienen. Una optimización posterior requiere localizar la etapa dominante en uso real y probar audios diversos con referencia, antes de cambiar la precisión o el contexto.

## Flujo diario con transcripción durante la grabación

Por petición explícita del usuario se activa por defecto cuando no haya una preferencia guardada; se respetan tanto true como false existentes. La interfaz explica la espera frente al trabajo durante captura. Se verificaron instalación nueva, campo ausente y persistencia de ambas elecciones. La configuración real consultada tenía Turbo, GPU y modo en vivo activados; los ocho dictados recientes revisados registraban ese modo (sin leer su contenido).

Prueba `--live-benchmark` con Turbo q8/Vulkan y cuatro minutos de audio sintético Spanglish repetido a ritmo real: procesamiento completo 10.721/12.315/10.499 s; mediana 10.721 s. Una ejecución en vivo completó ocho fragmentos, sin fallback, con espera final 2.221 s (aproximadamente 79% menor). Esto verifica la ventaja del modo existente en este fixture, no una aceleración nueva de 0.10.2. No incluye DeepSeek. Durante parte de la captura se ejecutaron pruebas ligeras de configuración/WPF; no es una medición de consumo en equipo inactivo. CPU acumulada del proceso durante captura y cierre 14.203 s; no mide GPU, energía ni ahorro total.

Los textos de lote y fragmentos difieren en 74 ediciones; no se interpreta la salida de lote como referencia correcta ni se afirma precisión idéntica. El fixture repite texto y corta al llegar a 240 s; sirve para medir espera, no para certificar precisión con habla natural. Las pruebas de integridad de PCM y recuperación comprueban que el audio se mantiene completo. La opción sigue identificada como experimental.

Fuente de implementación examinada: [WhisperProcessor en el commit de Whisper.net 1.9.0](https://github.com/sandrohanea/whisper.net/blob/094615b303e4db907ea2218c18e3965f5c9acbb2/Whisper.net/WhisperProcessor.cs).
