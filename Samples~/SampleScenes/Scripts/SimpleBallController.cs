using UnityEngine;

namespace InfiniteGrass
{
    public class SimpleBallController : MonoBehaviour
    {
        public Transform cameraTransform;
        public float rotationSpeed = 10;

        private Rigidbody _rb;
        private Vector3 _cameraOffset;

        private void Start()
        {
            _cameraOffset = cameraTransform.position - transform.position;
            _rb = GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F)) 
                _rb.isKinematic = !_rb.isKinematic;
        }
        
        private void FixedUpdate()
        {
            _rb.AddForce(cameraTransform.forward * Input.GetAxis("Vertical") * 0.1f, ForceMode.VelocityChange);
            _rb.AddForce(cameraTransform.right * Input.GetAxis("Horizontal") * 0.1f, ForceMode.VelocityChange);
            cameraTransform.position = transform.position + _cameraOffset;

            var r = 0f;

            if (Input.GetKey(KeyCode.E))
                r = 1;
            if (Input.GetKey(KeyCode.Q))
                r = -1;

            cameraTransform.rotation *= Quaternion.Euler(0, r * rotationSpeed, 0);
        }
    }
}