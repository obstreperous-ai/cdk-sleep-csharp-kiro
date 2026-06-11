"""Sleep Audio Processor Lambda handler.

Downloads input from S3, processes/generates audio content, uploads to output
bucket, updates DynamoDB with output metadata, and returns a structured response
for downstream pipeline steps. Uses structured JSON logging for observability.
"""

import json
import logging
import os
import time
import uuid

import boto3

logger = logging.getLogger()
logger.setLevel(logging.INFO)

dynamodb = boto3.resource("dynamodb")
s3_client = boto3.client("s3")

SUPPORTED_EXTENSIONS = (".mp3", ".wav", ".ogg", ".txt")

# Maximum allowed input file size (100 MB). Files larger than this are rejected
# before download to avoid Lambda memory/timeout issues.
MAX_INPUT_FILE_SIZE_BYTES = 100 * 1024 * 1024


def _log(level, message, request_id=None, audio_id=None, status=None, error=None, **kwargs):
    """Emit a structured JSON log entry."""
    entry = {
        "message": message,
        "request_id": request_id,
        "audio_id": audio_id,
        "status": status,
    }
    if error:
        entry["error"] = error
    entry.update(kwargs)
    # Remove None values for cleaner logs
    entry = {k: v for k, v in entry.items() if v is not None}
    if level == "error":
        logger.error(json.dumps(entry))
    else:
        logger.info(json.dumps(entry))


def _get_file_extension(key):
    """Extract and return the lowercase file extension from an S3 key."""
    lower_key = key.lower()
    for ext in SUPPORTED_EXTENSIONS:
        if lower_key.endswith(ext):
            return ext
    return None


def _generate_output_key(audio_id, extension):
    """Generate an output key with a clear naming convention.

    Format: processed/{audioId_without_ext}_{timestamp}_{uuid}.{ext}
    """
    # Strip extension from audio_id for the base name
    base_name = audio_id
    for ext in SUPPORTED_EXTENSIONS:
        if base_name.lower().endswith(ext):
            base_name = base_name[: -len(ext)]
            break

    # Clean up path separators for flat output structure
    base_name = base_name.replace("/", "_").replace("\\", "_")

    timestamp = int(time.time())
    unique_id = str(uuid.uuid4())[:8]

    # Output is always mp3 (processed audio format)
    output_ext = ".mp3" if extension != ".txt" else ".mp3"
    return f"processed/{base_name}_{timestamp}_{unique_id}{output_ext}"


def _download_input(bucket_name, key):
    """Download the input file from S3 and return its content and metadata."""
    response = s3_client.get_object(Bucket=bucket_name, Key=key)
    content = response["Body"].read()
    content_type = response.get("ContentType", "application/octet-stream")
    content_length = response.get("ContentLength", len(content))
    return content, content_type, content_length


def _process_content(content, extension, audio_id):
    """Process the input content based on file type.

    For text files (.txt): reads text content, prepares it for downstream
    Polly synthesis by the state machine. Returns the text as processed content
    with metadata indicating it is ready for TTS.

    For audio files (.mp3, .wav, .ogg): performs basic processing (metadata
    enrichment, normalization marker). Returns processed audio content with
    metadata about the file.

    Returns:
        tuple: (processed_content_bytes, metadata_dict)
    """
    metadata = {
        "sourceFormat": extension.lstrip("."),
        "outputFormat": "mp3",
        "audioId": audio_id,
    }

    if extension == ".txt":
        # Text input: read and prepare for Polly synthesis
        text_content = content.decode("utf-8", errors="replace")
        # Wrap text content with processing markers for the pipeline
        processed = json.dumps({
            "type": "text_for_synthesis",
            "text": text_content,
            "voice": "Joanna",
            "outputFormat": "mp3",
        }).encode("utf-8")
        metadata["contentType"] = "application/json"
        metadata["textLength"] = len(text_content)
        metadata["processingType"] = "text_to_speech_preparation"
    else:
        # Audio input: basic processing pass-through with metadata enrichment
        # In a production system, this would normalize, mix, or enhance audio.
        # For now, we pass through the audio content as the processed output.
        processed = content
        metadata["contentType"] = f"audio/{extension.lstrip('.')}"
        metadata["processingType"] = "audio_passthrough"

    metadata["outputSize"] = len(processed)
    return processed, metadata


def _upload_output(output_bucket, output_key, content, content_type):
    """Upload the processed content to the output S3 bucket."""
    s3_client.put_object(
        Bucket=output_bucket,
        Key=output_key,
        Body=content,
        ContentType=content_type,
    )


def _update_dynamodb_status(table, audio_id, output_bucket, output_key, file_size, metadata):
    """Update DynamoDB record with output location and processing metadata.

    Sets status to PROCESSED to indicate the Lambda has finished uploading
    the output file. This is distinct from PROCESSING (initial state machine
    status) and COMPLETED (set by downstream UpdateStatusCompleted step).
    """
    table.update_item(
        Key={"audioId": audio_id},
        UpdateExpression=(
            "SET #s = :status, outputBucket = :outputBucket, "
            "outputKey = :outputKey, fileSize = :fileSize, "
            "processingMetadata = :metadata"
        ),
        ExpressionAttributeNames={"#s": "status"},
        ExpressionAttributeValues={
            ":status": "PROCESSED",
            ":outputBucket": output_bucket,
            ":outputKey": output_key,
            ":fileSize": file_size,
            ":metadata": json.dumps(metadata),
        },
    )


def handler(event, context):
    """Process audio: download from S3, process, upload to output bucket, update DynamoDB.

    Args:
        event: Input from Step Functions containing S3 details and audioId.
        context: Lambda context object.

    Returns:
        dict: Response with processing status, output location, and metadata.
    """
    request_id = context.aws_request_id if context else None

    _log("info", "Received event", request_id=request_id, status="RECEIVED")

    # Validate environment variables
    table_name = os.environ.get("TABLE_NAME")
    if not table_name:
        _log("error", "TABLE_NAME environment variable is not set", request_id=request_id, status="ERROR")
        raise ValueError("TABLE_NAME environment variable is not set")

    output_bucket_name = os.environ.get("OUTPUT_BUCKET_NAME")
    if not output_bucket_name:
        _log("error", "OUTPUT_BUCKET_NAME environment variable is not set", request_id=request_id, status="ERROR")
        raise ValueError("OUTPUT_BUCKET_NAME environment variable is not set")

    # Validate required fields
    detail = event.get("detail")
    if not detail:
        _log("error", "Missing 'detail' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing 'detail' in event input")

    bucket_info = detail.get("bucket")
    if not bucket_info or not bucket_info.get("name"):
        _log("error", "Missing or empty 'detail.bucket.name' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing or empty 'detail.bucket.name' in event input")

    object_info = detail.get("object")
    if not object_info or not object_info.get("key"):
        _log("error", "Missing or empty 'detail.object.key' in event input", request_id=request_id, status="ERROR")
        raise ValueError("Missing or empty 'detail.object.key' in event input")

    bucket_name = bucket_info["name"]
    audio_id = object_info["key"]

    # Validate file extension
    extension = _get_file_extension(audio_id)
    if not extension:
        _log("error", "Unsupported file format", request_id=request_id, audio_id=audio_id, status="ERROR",
             error=f"Unsupported file format: '{audio_id}'. Supported extensions: {', '.join(SUPPORTED_EXTENSIONS)}")
        raise ValueError(
            f"Unsupported file format: '{audio_id}'. "
            f"Supported extensions: {', '.join(SUPPORTED_EXTENSIONS)}"
        )

    # Optionally validate bucket name matches INPUT_BUCKET_NAME
    input_bucket_name = os.environ.get("INPUT_BUCKET_NAME")
    if input_bucket_name and bucket_name != input_bucket_name:
        _log("error", "Unexpected bucket", request_id=request_id, audio_id=audio_id, status="ERROR",
             error=f"Unexpected bucket: '{bucket_name}'. Expected: '{input_bucket_name}'")
        raise ValueError(
            f"Unexpected bucket: '{bucket_name}'. Expected: '{input_bucket_name}'"
        )

    table = dynamodb.Table(table_name)

    try:
        # Step 0: Pre-flight size check to reject oversized files before download
        head_response = s3_client.head_object(Bucket=bucket_name, Key=audio_id)
        input_file_size = head_response.get("ContentLength", 0)

        if input_file_size > MAX_INPUT_FILE_SIZE_BYTES:
            _log("error", "Input file exceeds maximum allowed size",
                 request_id=request_id, audio_id=audio_id, status="ERROR",
                 input_size=input_file_size, max_size=MAX_INPUT_FILE_SIZE_BYTES)
            raise ValueError(
                f"Input file size ({input_file_size} bytes) exceeds maximum "
                f"allowed size ({MAX_INPUT_FILE_SIZE_BYTES} bytes)"
            )

        _log("info", "Downloading input from S3", request_id=request_id, audio_id=audio_id,
             status="DOWNLOADING", bucket=bucket_name)

        # Step 1: Download input from S3
        content, content_type, content_length = _download_input(bucket_name, audio_id)

        _log("info", "Input downloaded successfully", request_id=request_id, audio_id=audio_id,
             status="DOWNLOADED", input_size=content_length)

        # Step 2: Process content
        _log("info", "Processing content", request_id=request_id, audio_id=audio_id,
             status="PROCESSING", extension=extension)

        processed_content, processing_metadata = _process_content(content, extension, audio_id)

        _log("info", "Content processed successfully", request_id=request_id, audio_id=audio_id,
             status="PROCESSED", output_size=len(processed_content))

        # Step 3: Generate output key and upload to output bucket
        output_key = _generate_output_key(audio_id, extension)

        _log("info", "Uploading to output bucket", request_id=request_id, audio_id=audio_id,
             status="UPLOADING", output_bucket=output_bucket_name, output_key=output_key)

        output_content_type = processing_metadata.get("contentType", "application/octet-stream")
        _upload_output(output_bucket_name, output_key, processed_content, output_content_type)

        _log("info", "Upload complete", request_id=request_id, audio_id=audio_id,
             status="UPLOADED", output_bucket=output_bucket_name, output_key=output_key)

        # Step 4: Update DynamoDB with output metadata
        # If DynamoDB update fails after a successful S3 upload, the output object
        # becomes orphaned (no DynamoDB record points to it). We log the output key
        # in a structured error entry so orphaned objects are discoverable via
        # CloudWatch Logs Insights queries. A periodic cleanup job or S3 lifecycle
        # policy should handle removal of orphaned objects.
        file_size = len(processed_content)
        try:
            _update_dynamodb_status(table, audio_id, output_bucket_name, output_key, file_size, processing_metadata)
        except Exception as dynamo_err:
            _log("error", "DynamoDB update failed after successful S3 upload; output object is orphaned",
                 request_id=request_id, audio_id=audio_id, status="ORPHANED_OUTPUT",
                 error=str(dynamo_err), output_bucket=output_bucket_name, output_key=output_key,
                 file_size=file_size)
            raise

        _log("info", "DynamoDB updated with output metadata", request_id=request_id,
             audio_id=audio_id, status="PROCESSED")

        # Step 5: Return structured response for downstream state machine steps
        response = {
            "statusCode": 200,
            "body": {
                "audioId": audio_id,
                "inputBucket": bucket_name,
                "outputBucket": output_bucket_name,
                "outputKey": output_key,
                "status": "PROCESSED",
                "fileSize": file_size,
                "metadata": processing_metadata,
                "message": "Audio processed and uploaded successfully",
            },
        }

        _log("info", "Processing complete", request_id=request_id, audio_id=audio_id,
             status="PROCESSED", output_key=output_key, file_size=file_size)

        return response

    except Exception as e:
        _log("error", "Error processing audio", request_id=request_id,
             audio_id=audio_id, status="ERROR", error=str(e))
        raise
